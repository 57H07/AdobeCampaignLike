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
/// US-006 TASK-006-05: Unit tests for DOCX file storage.
///
/// Validates:
/// - DocxFilePathHelper naming convention (templates/{id}/v{version}.docx)
/// - TemplateService.CreateAsync writes DOCX via ITemplateBodyStore when DocxContent is provided
/// - TemplateService.UpdateAsync writes DOCX to the next version path when DocxContent is provided
/// - Non-DOCX flows (DocxContent = null) leave BodyPath unchanged from the request
/// - Directory structure is derived from the path (auto-create is the store's responsibility)
/// - File path stored is relative (excludes storage root)
/// </summary>
public class DocxStorageTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly TemplateService _service;
    private readonly Mock<ITemplateBodyStore> _bodyStoreMock;

    public DocxStorageTests()
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
    // DocxFilePathHelper — naming convention (TASK-006-03)
    // ----------------------------------------------------------------

    [Fact]
    public void DocxFilePathHelper_Build_ReturnsCorrectRelativePath()
    {
        // Arrange
        var templateId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        const int version = 1;

        // Act
        var path = DocxFilePathHelper.Build(templateId, version);

        // Assert — convention: templates/{templateId}/v{version}.docx
        path.Should().Be($"templates/{templateId}/v1.docx");
    }

    [Theory]
    [InlineData(1, "v1.docx")]
    [InlineData(2, "v2.docx")]
    [InlineData(10, "v10.docx")]
    [InlineData(99, "v99.docx")]
    public void DocxFilePathHelper_Build_VersionSegmentMatchesConvention(int version, string expectedFileName)
    {
        // Arrange
        var templateId = Guid.NewGuid();

        // Act
        var path = DocxFilePathHelper.Build(templateId, version);

        // Assert — file name matches v{version}.docx
        path.Should().EndWith($"/{expectedFileName}");
        path.Should().StartWith($"templates/{templateId}/");
    }

    [Fact]
    public void DocxFilePathHelper_Build_ThrowsForEmptyGuid()
    {
        // Act
        var act = () => DocxFilePathHelper.Build(Guid.Empty, 1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("templateId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void DocxFilePathHelper_Build_ThrowsForVersionLessThanOne(int invalidVersion)
    {
        // Act
        var act = () => DocxFilePathHelper.Build(Guid.NewGuid(), invalidVersion);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("version");
    }

    [Fact]
    public void DocxFilePathHelper_Build_PathIsRelative_NoLeadingSlash()
    {
        // Act
        var path = DocxFilePathHelper.Build(Guid.NewGuid(), 1);

        // Assert — relative path must not start with a directory separator
        path.Should().NotStartWith("/");
        path.Should().NotStartWith("\\");
    }

    [Fact]
    public void DocxFilePathHelper_Build_PathContainsDocxExtension()
    {
        // Act
        var path = DocxFilePathHelper.Build(Guid.NewGuid(), 3);

        // Assert
        path.Should().EndWith(".docx");
    }

    // ----------------------------------------------------------------
    // CreateAsync — DOCX storage (TASK-006-01)
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithDocxContent_CallsBodyStoreWrite()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // DOCX magic bytes
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template",
            Channel = ChannelType.Letter,
            BodyPath = string.Empty, // Will be overridden by service
            DocxContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — WriteAsync was called exactly once
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), stream, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithDocxContent_SetsBodyPathToConventionalPath()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        var request = new CreateTemplateRequest
        {
            Name = "DOCX Letter",
            Channel = ChannelType.Letter,
            BodyPath = string.Empty,
            DocxContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — BodyPath follows templates/{templateId}/v1.docx convention
        var expectedPath = DocxFilePathHelper.Build(result.Id, 1);
        result.BodyPath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task CreateAsync_WithDocxContent_PathStartsWithTemplatesFolder()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        var request = new CreateTemplateRequest
        {
            Name = "DOCX Folder Check",
            Channel = ChannelType.Letter,
            DocxContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — path uses templates/{id}/vN.docx structure
        result.BodyPath.Should().StartWith("templates/");
        result.BodyPath.Should().EndWith("/v1.docx");
        result.BodyPath.Should().NotStartWith("/"); // relative, not absolute
    }

    [Fact]
    public async Task CreateAsync_WithDocxContent_VersionIsAlwaysOne()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B });
        var request = new CreateTemplateRequest
        {
            Name = "First Version",
            Channel = ChannelType.Letter,
            DocxContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Version.Should().Be(1);
        result.BodyPath.Should().Contain("/v1.docx");
    }

    [Fact]
    public async Task CreateAsync_WithoutDocxContent_UsesBodyPathFromRequest()
    {
        // Arrange — Email template with no DOCX content
        var request = new CreateTemplateRequest
        {
            Name = "Email Template",
            Channel = ChannelType.Email,
            BodyPath = "templates/email-template/v1.html",
            DocxContent = null
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — BodyPath comes from request, store was NOT called
        result.BodyPath.Should().Be("templates/email-template/v1.html");
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithDocxContent_WrittenPathPassedToStore()
    {
        // Arrange — capture the path argument passed to WriteAsync
        string? capturedPath = null;
        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((path, _, _) => capturedPath = path)
            .ReturnsAsync((string p, Stream _, CancellationToken _) => p);

        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B });
        var request = new CreateTemplateRequest
        {
            Name = "Capture Test",
            Channel = ChannelType.Letter,
            DocxContent = stream
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — the path passed to WriteAsync matches the saved BodyPath
        capturedPath.Should().NotBeNullOrEmpty();
        capturedPath.Should().Be(result.BodyPath);
        capturedPath.Should().Be(DocxFilePathHelper.Build(result.Id, 1));
    }

    // ----------------------------------------------------------------
    // UpdateAsync — DOCX versioning (TASK-006-02)
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WithDocxContent_WritesToNextVersionPath()
    {
        // Arrange — create a v1 template first
        using var createStream = new MemoryStream(new byte[] { 0x50, 0x4B });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Versioned Letter",
            Channel = ChannelType.Letter,
            DocxContent = createStream
        });

        // Reset mock to verify second call
        _bodyStoreMock.Invocations.Clear();

        // Act — update with new DOCX content
        using var updateStream = new MemoryStream(new byte[] { 0x50, 0x4B, 0xFF });
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Versioned Letter",
            BodyPath = created.BodyPath, // will be overridden
            DocxContent = updateStream
        });

        // Assert — new version path: v2
        var expectedPath = DocxFilePathHelper.Build(created.Id, 2);
        updated.BodyPath.Should().Be(expectedPath);
        updated.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_WithDocxContent_CallsBodyStoreWriteOnce()
    {
        // Arrange
        using var createStream = new MemoryStream(new byte[] { 0x50, 0x4B });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Write Count Check",
            Channel = ChannelType.Letter,
            DocxContent = createStream
        });

        _bodyStoreMock.Invocations.Clear();

        using var updateStream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x01 });

        // Act
        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Write Count Check",
            BodyPath = created.BodyPath,
            DocxContent = updateStream
        });

        // Assert — exactly one write for the update
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), updateStream, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithoutDocxContent_DoesNotCallBodyStore()
    {
        // Arrange — create template with path already set
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "No File Update",
            Channel = ChannelType.Email,
            BodyPath = "templates/no-file-update/v1.html"
        });

        _bodyStoreMock.Invocations.Clear();

        // Act — update without new file (metadata-only change)
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "No File Update Renamed",
            BodyPath = "templates/no-file-update/v1.html",
            DocxContent = null
        });

        // Assert — store not called, BodyPath kept from request
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
        updated.BodyPath.Should().Be("templates/no-file-update/v1.html");
    }
}
