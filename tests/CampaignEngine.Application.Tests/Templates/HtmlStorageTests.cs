using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// US-007 TASK-007-04/05/06: Unit tests for HTML file storage for Email and SMS templates.
///
/// Validates:
/// - HtmlFilePathHelper naming convention (templates/{id}/v{version}.html)
/// - TemplateService.CreateAsync writes HTML via ITemplateBodyStore when HtmlContent is provided (Email)
/// - TemplateService.CreateAsync writes HTML via ITemplateBodyStore when HtmlContent is provided (SMS)
/// - TemplateService.UpdateAsync writes HTML to the next version path when HtmlContent is provided
/// - Non-HTML flows (HtmlContent = null) leave BodyPath unchanged from the request
/// - DOCX flow (DocxContent set) takes precedence over HtmlContent
/// - File path stored is relative (excludes storage root)
/// - Regression: existing Email/SMS creation without HtmlContent stream continues to work
/// </summary>
public class HtmlStorageTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly TemplateService _service;
    private readonly Mock<ITemplateBodyStore> _bodyStoreMock;

    public HtmlStorageTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        _bodyStoreMock = new Mock<ITemplateBodyStore>();
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);

        // Default: WriteAsync returns the supplied path unchanged
        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object,
            manifestService.Object, parserService.Object,
            _bodyStoreMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // HtmlFilePathHelper — naming convention (TASK-007-01)
    // ----------------------------------------------------------------

    [Fact]
    public void HtmlFilePathHelper_Build_ReturnsCorrectRelativePath()
    {
        // Arrange
        var templateId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        const int version = 1;

        // Act
        var path = HtmlFilePathHelper.Build(templateId, version);

        // Assert — convention: templates/{templateId}/v{version}.html
        path.Should().Be($"templates/{templateId}/v1.html");
    }

    [Theory]
    [InlineData(1, "v1.html")]
    [InlineData(2, "v2.html")]
    [InlineData(10, "v10.html")]
    [InlineData(99, "v99.html")]
    public void HtmlFilePathHelper_Build_VersionSegmentMatchesConvention(int version, string expectedFileName)
    {
        // Arrange
        var templateId = Guid.NewGuid();

        // Act
        var path = HtmlFilePathHelper.Build(templateId, version);

        // Assert — file name matches v{version}.html
        path.Should().EndWith($"/{expectedFileName}");
        path.Should().StartWith($"templates/{templateId}/");
    }

    [Fact]
    public void HtmlFilePathHelper_Build_ThrowsForEmptyGuid()
    {
        // Act
        var act = () => HtmlFilePathHelper.Build(Guid.Empty, 1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("templateId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void HtmlFilePathHelper_Build_ThrowsForVersionLessThanOne(int invalidVersion)
    {
        // Act
        var act = () => HtmlFilePathHelper.Build(Guid.NewGuid(), invalidVersion);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("version");
    }

    [Fact]
    public void HtmlFilePathHelper_Build_PathIsRelative_NoLeadingSlash()
    {
        // Act
        var path = HtmlFilePathHelper.Build(Guid.NewGuid(), 1);

        // Assert — relative path must not start with a directory separator
        path.Should().NotStartWith("/");
        path.Should().NotStartWith("\\");
    }

    [Fact]
    public void HtmlFilePathHelper_Build_PathContainsHtmlExtension()
    {
        // Act
        var path = HtmlFilePathHelper.Build(Guid.NewGuid(), 3);

        // Assert
        path.Should().EndWith(".html");
    }

    // ----------------------------------------------------------------
    // TASK-007-04: Email template — CreateAsync with HtmlContent
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_EmailWithHtmlContent_CallsBodyStoreWrite()
    {
        // Arrange
        using var stream = new MemoryStream("<html><body>Hello {{ name }}!</body></html>"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Welcome Email",
            Channel = ChannelType.Email,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — WriteAsync was called exactly once
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), stream, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EmailWithHtmlContent_SetsBodyPathToConventionalPath()
    {
        // Arrange
        using var stream = new MemoryStream("<html><body>Hello!</body></html>"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Email Body Path Test",
            Channel = ChannelType.Email,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — BodyPath follows templates/{templateId}/v1.html convention
        var expectedPath = HtmlFilePathHelper.Build(result.Id, 1);
        result.BodyPath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task CreateAsync_EmailWithHtmlContent_PathStartsWithTemplatesFolder()
    {
        // Arrange
        using var stream = new MemoryStream("<p>Hello</p>"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Email Folder Check",
            Channel = ChannelType.Email,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — path uses templates/{id}/vN.html structure
        result.BodyPath.Should().StartWith("templates/");
        result.BodyPath.Should().EndWith("/v1.html");
        result.BodyPath.Should().NotStartWith("/"); // relative, not absolute
    }

    [Fact]
    public async Task CreateAsync_EmailWithHtmlContent_VersionIsAlwaysOne()
    {
        // Arrange
        using var stream = new MemoryStream("<p>First</p>"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Email Version One",
            Channel = ChannelType.Email,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Version.Should().Be(1);
        result.BodyPath.Should().Contain("/v1.html");
    }

    [Fact]
    public async Task CreateAsync_EmailWithHtmlContent_WrittenPathPassedToStore()
    {
        // Arrange — capture the path argument passed to WriteAsync
        string? capturedPath = null;
        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((path, _, _) => capturedPath = path)
            .ReturnsAsync((string p, Stream _, CancellationToken _) => p);

        using var stream = new MemoryStream("<p>Capture test</p>"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Email Capture Test",
            Channel = ChannelType.Email,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — the path passed to WriteAsync matches the saved BodyPath
        capturedPath.Should().NotBeNullOrEmpty();
        capturedPath.Should().Be(result.BodyPath);
        capturedPath.Should().Be(HtmlFilePathHelper.Build(result.Id, 1));
    }

    // ----------------------------------------------------------------
    // TASK-007-05: SMS template — CreateAsync with HtmlContent
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_SmsWithHtmlContent_CallsBodyStoreWrite()
    {
        // Arrange
        using var stream = new MemoryStream("Your code is {{ code }}."u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "SMS OTP Template",
            Channel = ChannelType.Sms,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — WriteAsync was called exactly once
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), stream, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_SmsWithHtmlContent_SetsBodyPathToConventionalPath()
    {
        // Arrange
        using var stream = new MemoryStream("Hello {{ name }}, welcome."u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "SMS Welcome",
            Channel = ChannelType.Sms,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — BodyPath follows templates/{templateId}/v1.html convention (same as Email)
        var expectedPath = HtmlFilePathHelper.Build(result.Id, 1);
        result.BodyPath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task CreateAsync_SmsWithHtmlContent_PathEndsWithHtmlExtension()
    {
        // Arrange
        using var stream = new MemoryStream("Dear {{ name }}"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "SMS Extension Test",
            Channel = ChannelType.Sms,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — SMS templates use .html extension (not .sms or .txt)
        result.BodyPath.Should().EndWith(".html");
    }

    // ----------------------------------------------------------------
    // TASK-007-02: UpdateAsync — HTML versioning
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_EmailWithHtmlContent_WritesToNextVersionPath()
    {
        // Arrange — create a v1 Email template first
        using var createStream = new MemoryStream("<p>Version 1</p>"u8.ToArray());
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Versioned Email",
            Channel = ChannelType.Email,
            HtmlContent = createStream
        });

        // Reset mock to verify second call
        _bodyStoreMock.Invocations.Clear();

        // Act — update with new HTML content
        using var updateStream = new MemoryStream("<p>Version 2</p>"u8.ToArray());
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Versioned Email",
            BodyPath = created.BodyPath, // will be overridden
            HtmlContent = updateStream
        });

        // Assert — new version path: v2
        var expectedPath = HtmlFilePathHelper.Build(created.Id, 2);
        updated.BodyPath.Should().Be(expectedPath);
        updated.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_SmsWithHtmlContent_WritesToNextVersionPath()
    {
        // Arrange — create a v1 SMS template first
        using var createStream = new MemoryStream("SMS v1"u8.ToArray());
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Versioned SMS",
            Channel = ChannelType.Sms,
            HtmlContent = createStream
        });

        _bodyStoreMock.Invocations.Clear();

        // Act — update with new SMS content
        using var updateStream = new MemoryStream("SMS v2"u8.ToArray());
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Versioned SMS",
            BodyPath = created.BodyPath,
            HtmlContent = updateStream
        });

        // Assert — new version path: v2
        var expectedPath = HtmlFilePathHelper.Build(created.Id, 2);
        updated.BodyPath.Should().Be(expectedPath);
        updated.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_EmailWithHtmlContent_CallsBodyStoreWriteOnce()
    {
        // Arrange
        using var createStream = new MemoryStream("<p>v1</p>"u8.ToArray());
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Write Count Email",
            Channel = ChannelType.Email,
            HtmlContent = createStream
        });

        _bodyStoreMock.Invocations.Clear();

        using var updateStream = new MemoryStream("<p>v2</p>"u8.ToArray());

        // Act
        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Write Count Email",
            BodyPath = created.BodyPath,
            HtmlContent = updateStream
        });

        // Assert — exactly one write for the update
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), updateStream, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // TASK-007-06: Regression — existing Email/SMS workflow without HtmlContent stream
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_EmailWithoutHtmlContent_UsesBodyPathFromRequest()
    {
        // Arrange — Email template with no HTML content stream (legacy path)
        var request = new CreateTemplateRequest
        {
            Name = "Legacy Email",
            Channel = ChannelType.Email,
            BodyPath = "templates/legacy-email/v1.html",
            HtmlContent = null
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — BodyPath comes from request, store was NOT called
        result.BodyPath.Should().Be("templates/legacy-email/v1.html");
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SmsWithoutHtmlContent_UsesBodyPathFromRequest()
    {
        // Arrange — SMS template with no HTML content stream (legacy path)
        var request = new CreateTemplateRequest
        {
            Name = "Legacy SMS",
            Channel = ChannelType.Sms,
            BodyPath = "templates/legacy-sms/v1.html",
            HtmlContent = null
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — BodyPath comes from request, store was NOT called
        result.BodyPath.Should().Be("templates/legacy-sms/v1.html");
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_EmailWithoutHtmlContent_DoesNotCallBodyStore()
    {
        // Arrange — create template with path already set
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "No File Email",
            Channel = ChannelType.Email,
            BodyPath = "templates/no-file-email/v1.html"
        });

        _bodyStoreMock.Invocations.Clear();

        // Act — update without new file (metadata-only change)
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "No File Email Renamed",
            BodyPath = "templates/no-file-email/v1.html",
            HtmlContent = null
        });

        // Assert — store not called, BodyPath kept from request
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
        updated.BodyPath.Should().Be("templates/no-file-email/v1.html");
    }

    [Fact]
    public async Task CreateAsync_LetterChannelNotAffectedByHtmlPath()
    {
        // Arrange — Letter template with DOCX, not HTML
        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // DOCX magic
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template",
            Channel = ChannelType.Letter,
            DocxContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — Letter path uses .docx, not .html
        result.BodyPath.Should().EndWith(".docx");
        result.BodyPath.Should().NotEndWith(".html");
    }

    [Fact]
    public async Task CreateAsync_DocxContentTakesPrecedenceOverHtmlContent()
    {
        // Arrange — both DocxContent and HtmlContent supplied (DocxContent wins per service logic)
        using var docxStream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        using var htmlStream = new MemoryStream("<p>HTML</p>"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Priority Test",
            Channel = ChannelType.Letter,
            DocxContent = docxStream,
            HtmlContent = htmlStream  // should be ignored when DocxContent is set
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — DOCX branch was taken (path ends with .docx)
        result.BodyPath.Should().EndWith(".docx");
    }

    [Fact]
    public async Task CreateAsync_EmailStatus_IsAlwaysDraft()
    {
        // Arrange
        using var stream = new MemoryStream("<p>Email body</p>"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Draft Status Email",
            Channel = ChannelType.Email,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — all newly-created templates start as Draft regardless of channel
        result.Status.Should().Be(Domain.Enums.TemplateStatus.Draft);
    }

    [Fact]
    public async Task CreateAsync_SmsStatus_IsAlwaysDraft()
    {
        // Arrange
        using var stream = new MemoryStream("SMS body"u8.ToArray());
        var request = new CreateTemplateRequest
        {
            Name = "Draft Status SMS",
            Channel = ChannelType.Sms,
            HtmlContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Status.Should().Be(Domain.Enums.TemplateStatus.Draft);
    }
}
