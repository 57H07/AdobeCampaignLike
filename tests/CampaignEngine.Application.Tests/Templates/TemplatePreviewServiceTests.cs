using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Unit tests for TemplatePreviewService (US-010).
/// Validates: sample data fetching, template resolution, missing placeholder detection,
/// channel post-processing, and error handling.
/// </summary>
public class TemplatePreviewServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly Mock<IConnectionStringEncryptor> _encryptorMock;
    private readonly Mock<IDataSourceConnector> _connectorMock;
    private readonly Mock<IDataSourceConnectorRegistry> _connectorRegistryMock;
    private readonly Mock<ISubTemplateResolverService> _subTemplateResolverMock;
    private readonly Mock<ITemplateRenderer> _rendererMock;
    private readonly Mock<IPlaceholderParserService> _parserMock;
    private readonly Mock<IChannelPostProcessorRegistry> _postProcessorRegistryMock;
    private readonly Mock<IChannelPostProcessor> _postProcessorMock;
    private readonly Mock<IAppLogger<TemplatePreviewService>> _loggerMock;
    private readonly TemplatePreviewService _service;

    public TemplatePreviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);
        _encryptorMock = new Mock<IConnectionStringEncryptor>();
        _connectorMock = new Mock<IDataSourceConnector>();
        _connectorRegistryMock = new Mock<IDataSourceConnectorRegistry>();
        _connectorRegistryMock
            .Setup(r => r.GetConnector(It.IsAny<CampaignEngine.Domain.Enums.DataSourceType>()))
            .Returns(_connectorMock.Object);
        _subTemplateResolverMock = new Mock<ISubTemplateResolverService>();
        _rendererMock = new Mock<ITemplateRenderer>();
        _parserMock = new Mock<IPlaceholderParserService>();
        _postProcessorRegistryMock = new Mock<IChannelPostProcessorRegistry>();
        _postProcessorMock = new Mock<IChannelPostProcessor>();
        _loggerMock = new Mock<IAppLogger<TemplatePreviewService>>();

        var templateRepository = new TemplateRepository(_context);
        var dataSourceRepository = new DataSourceRepository(_context);
        _service = new TemplatePreviewService(
            templateRepository,
            dataSourceRepository,
            _encryptorMock.Object,
            _connectorRegistryMock.Object,
            _subTemplateResolverMock.Object,
            _rendererMock.Object,
            _parserMock.Object,
            _postProcessorRegistryMock.Object,
            _loggerMock.Object);

        // Default: encryptor returns plaintext unchanged for test simplicity
        _encryptorMock
            .Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns<string>(s => s);

        // Default: sub-template resolver returns the same HTML (no sub-templates)
        _subTemplateResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<Guid, string, CancellationToken, ISubTemplateResolverService, string>(
                (_, html, _) => html);

        // Default: renderer returns "[rendered: {html}]"
        _rendererMock
            .Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<string, IDictionary<string, object?>, CancellationToken, ITemplateRenderer, string>(
                (html, _, _) => $"[rendered]{html}[/rendered]");

        // Default: parser returns empty extraction
        _parserMock
            .Setup(p => p.ExtractPlaceholders(It.IsAny<string>()))
            .Returns(new PlaceholderExtractionResult
            {
                ScalarKeys = [],
                IterationKeys = [],
                AllKeys = []
            });

        // Default post-processor returns text/html
        _postProcessorMock.Setup(p => p.Channel).Returns(ChannelType.Email);
        _postProcessorMock
            .Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<PostProcessingContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<string, PostProcessingContext?, CancellationToken, IChannelPostProcessor, PostProcessingResult>(
                (html, _, _) => PostProcessingResult.Text(html, "text/html"));

        _postProcessorRegistryMock
            .Setup(r => r.HasProcessor(It.IsAny<ChannelType>()))
            .Returns(true);
        _postProcessorRegistryMock
            .Setup(r => r.GetProcessor(It.IsAny<ChannelType>()))
            .Returns(_postProcessorMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // Helper factories
    // ----------------------------------------------------------------

    private async Task<Template> SeedTemplateAsync(
        string htmlBody = "<p>Hello {{ name }}</p>",
        ChannelType channel = ChannelType.Email)
    {
        var template = new Template
        {
            Name = "Test Template",
            Channel = channel,
            BodyPath = htmlBody,
            Status = TemplateStatus.Draft
        };
        _context.Templates.Add(template);
        await _context.SaveChangesAsync();
        return template;
    }

    private async Task<DataSource> SeedDataSourceAsync(string connectionString = "Server=test;Database=db")
    {
        var ds = new DataSource
        {
            Name = "Test DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = connectionString,
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();
        return ds;
    }

    private static IReadOnlyList<IDictionary<string, object?>> BuildRows(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (IDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["name"] = $"User{i}",
                ["email"] = $"user{i}@example.com"
            })
            .ToList();

    // ----------------------------------------------------------------
    // TASK-010-02: Sample data fetcher — fetches N rows
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewAsync_ValidRequest_ReturnsSampleRows()
    {
        // Arrange
        var template = await SeedTemplateAsync();
        var ds = await SeedDataSourceAsync();
        var rows = BuildRows(3);

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var request = new TemplatePreviewRequest
        {
            DataSourceId = ds.Id,
            SampleRowCount = 5,
            RowIndex = 0
        };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TotalSampleRows.Should().Be(3);
        result.SampleRows.Should().HaveCount(3);
    }

    [Fact]
    public async Task PreviewAsync_SampleRowCount_CappedAtFive()
    {
        // Arrange
        var template = await SeedTemplateAsync();
        var ds = await SeedDataSourceAsync();

        // Data source returns 10 rows but we only store up to 5
        var rows = BuildRows(10);
        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var request = new TemplatePreviewRequest
        {
            DataSourceId = ds.Id,
            SampleRowCount = 5,
            RowIndex = 0
        };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Business rule: max 5 sample rows
        result.TotalSampleRows.Should().Be(5);
    }

    // ----------------------------------------------------------------
    // TASK-010-03: Template resolution — renders with sample row
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewAsync_RendersWithSelectedRow()
    {
        // Arrange
        var template = await SeedTemplateAsync("<p>Hello {{ name }}</p>");
        var ds = await SeedDataSourceAsync();
        var rows = BuildRows(3);

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        // Use row index 1 (second row)
        var request = new TemplatePreviewRequest
        {
            DataSourceId = ds.Id,
            SampleRowCount = 3,
            RowIndex = 1
        };

        IDictionary<string, object?>? capturedData = null;
        _rendererMock
            .Setup(r => r.RenderAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IDictionary<string, object?>, CancellationToken>(
                (_, data, _) => capturedData = data)
            .ReturnsAsync("rendered output");

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowUsed.Should().Be(1);
        capturedData.Should().NotBeNull();
        capturedData!["name"].Should().Be("User1");
    }

    [Fact]
    public async Task PreviewAsync_RowIndex_ClampedToAvailableRows()
    {
        // Arrange
        var template = await SeedTemplateAsync();
        var ds = await SeedDataSourceAsync();
        var rows = BuildRows(2);  // Only 2 rows

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        // Request row index 4 but only 2 rows available — should clamp to 1
        var request = new TemplatePreviewRequest
        {
            DataSourceId = ds.Id,
            SampleRowCount = 2,
            RowIndex = 4
        };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowUsed.Should().Be(1); // Clamped from 4 to max index 1
    }

    // ----------------------------------------------------------------
    // Business rule: Missing placeholder detection (BR-US010-4)
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewAsync_MissingPlaceholders_ReturnedInResult()
    {
        // Arrange
        var template = await SeedTemplateAsync("<p>Hello {{ name }}, your code is {{ code }}</p>");
        var ds = await SeedDataSourceAsync();
        var rows = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["name"] = "Alice" }
            // "code" key is missing from the data
        };

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        _parserMock
            .Setup(p => p.ExtractPlaceholders(It.IsAny<string>()))
            .Returns(new PlaceholderExtractionResult
            {
                ScalarKeys = ["name", "code"],
                IterationKeys = [],
                AllKeys = ["name", "code"]
            });

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 1, RowIndex = 0 };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.MissingPlaceholders.Should().ContainSingle().Which.Should().Be("code");
    }

    [Fact]
    public async Task PreviewAsync_AllPlaceholdersPresent_NoMissingKeys()
    {
        // Arrange
        var template = await SeedTemplateAsync("<p>Hello {{ name }}</p>");
        var ds = await SeedDataSourceAsync();
        var rows = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["name"] = "Alice" }
        };

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        _parserMock
            .Setup(p => p.ExtractPlaceholders(It.IsAny<string>()))
            .Returns(new PlaceholderExtractionResult
            {
                ScalarKeys = ["name"],
                IterationKeys = [],
                AllKeys = ["name"]
            });

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 1, RowIndex = 0 };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.MissingPlaceholders.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Business rule: channel post-processing applied (BR-US010-3)
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewAsync_EmailChannel_AppliesCssInlining()
    {
        // Arrange
        var template = await SeedTemplateAsync(channel: ChannelType.Email);
        var ds = await SeedDataSourceAsync();
        var rows = BuildRows(1);

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        _rendererMock
            .Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>Hello</p>");

        _postProcessorMock
            .Setup(p => p.ProcessAsync("<p>Hello</p>", It.IsAny<PostProcessingContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostProcessingResult.Text("<p style=\"margin:0\">Hello</p>", "text/html"));

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 1, RowIndex = 0 };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ContentType.Should().Be("text/html");
        result.TextContent.Should().Contain("style=");
        _postProcessorMock.Verify(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<PostProcessingContext?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviewAsync_LetterChannel_ReturnsPdfBase64()
    {
        // Arrange
        var template = await SeedTemplateAsync(channel: ChannelType.Letter);
        var ds = await SeedDataSourceAsync();
        var rows = BuildRows(1);

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        _rendererMock
            .Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>Letter body</p>");

        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF header
        var letterProcessor = new Mock<IChannelPostProcessor>();
        letterProcessor.Setup(p => p.Channel).Returns(ChannelType.Letter);
        letterProcessor
            .Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<PostProcessingContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostProcessingResult.Binary(pdfBytes));

        _postProcessorRegistryMock
            .Setup(r => r.HasProcessor(ChannelType.Letter))
            .Returns(true);
        _postProcessorRegistryMock
            .Setup(r => r.GetProcessor(ChannelType.Letter))
            .Returns(letterProcessor.Object);

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 1, RowIndex = 0 };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ContentType.Should().Be("application/pdf");
        result.Base64Content.Should().NotBeNullOrEmpty();
        result.TextContent.Should().BeNull();
        Convert.FromBase64String(result.Base64Content!).Should().BeEquivalentTo(pdfBytes);
    }

    // ----------------------------------------------------------------
    // Error handling
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewAsync_TemplateNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var request = new TemplatePreviewRequest
        {
            DataSourceId = Guid.NewGuid(),
            SampleRowCount = 5,
            RowIndex = 0
        };

        // Act & Assert
        await _service
            .Invoking(s => s.PreviewAsync(Guid.NewGuid(), request))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task PreviewAsync_DataSourceNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var template = await SeedTemplateAsync();

        var request = new TemplatePreviewRequest
        {
            DataSourceId = Guid.NewGuid(), // Non-existent DS
            SampleRowCount = 5,
            RowIndex = 0
        };

        // Act & Assert
        await _service
            .Invoking(s => s.PreviewAsync(template.Id, request))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task PreviewAsync_DataSourceReturnsNoRows_ReturnsFailResult()
    {
        // Arrange
        var template = await SeedTemplateAsync();
        var ds = await SeedDataSourceAsync();

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IDictionary<string, object?>>());

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 5, RowIndex = 0 };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no rows");
    }

    [Fact]
    public async Task PreviewAsync_ConnectorThrows_ReturnsFailResult()
    {
        // Arrange
        var template = await SeedTemplateAsync();
        var ds = await SeedDataSourceAsync();

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 5, RowIndex = 0 };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task PreviewAsync_RendererThrows_ReturnsFailResult()
    {
        // Arrange
        var template = await SeedTemplateAsync();
        var ds = await SeedDataSourceAsync();
        var rows = BuildRows(1);

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        _rendererMock
            .Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Template syntax error"));

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 1, RowIndex = 0 };

        // Act
        var result = await _service.PreviewAsync(template.Id, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("rendering failed");
    }

    // ----------------------------------------------------------------
    // Business rule: preview is read-only (no DbContext writes)
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewAsync_DoesNotWriteToDatabase()
    {
        // Arrange
        var template = await SeedTemplateAsync();
        var ds = await SeedDataSourceAsync();
        var rows = BuildRows(1);
        var initialChangeCount = _context.ChangeTracker.Entries().Count();

        _connectorMock
            .Setup(c => c.QueryAsync(
                It.IsAny<DataSourceDefinitionDto>(),
                It.IsAny<IReadOnlyList<FilterExpressionDto>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var request = new TemplatePreviewRequest { DataSourceId = ds.Id, SampleRowCount = 1, RowIndex = 0 };

        // Act
        await _service.PreviewAsync(template.Id, request);

        // Assert: no pending changes added by the preview operation
        var modifiedEntries = _context.ChangeTracker.Entries()
            .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added
                     || e.State == Microsoft.EntityFrameworkCore.EntityState.Modified
                     || e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted)
            .ToList();

        modifiedEntries.Should().BeEmpty("preview must be read-only and not modify the database");
    }
}
