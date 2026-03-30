using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates; // DocxFilePathHelper + TemplateService
using CampaignEngine.Infrastructure.Tests.Persistence;

namespace CampaignEngine.Infrastructure.Tests.Templates;

/// <summary>
/// US-006 TASK-006-06: Integration tests for DOCX version increment.
///
/// Tests the full create → update → update cycle to verify that:
/// - Version numbers increment correctly (1, 2, 3, ...)
/// - Each version gets a distinct path following the convention
/// - The BodyPath written to the database is the relative versioned path
/// - Previous version files are not overwritten (each version is a new path)
/// - History snapshots preserve the correct BodyPath for each version
/// </summary>
public class DocxVersioningIntegrationTests : DbContextTestBase
{
    private readonly TemplateService _service;
    private readonly Mock<ITemplateBodyStore> _bodyStoreMock;

    public DocxVersioningIntegrationTests()
    {
        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        _bodyStoreMock = new Mock<ITemplateBodyStore>();

        // Default: WriteAsync returns the path argument unchanged
        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        var templateRepository = new TemplateRepository(Context);
        var unitOfWork = new UnitOfWork(Context);

        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object,
            manifestService.Object, parserService.Object,
            _bodyStoreMock.Object);
    }

    // ----------------------------------------------------------------
    // Version increment: 1 → 2 → 3
    // ----------------------------------------------------------------

    [Fact]
    public async Task Create_ThenUpdateTwice_VersionIncrements_PathVersioned()
    {
        // Arrange — create v1
        using var v1Stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x01 });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Multi-version Letter",
            Channel = ChannelType.Letter,
            DocxContent = v1Stream
        });

        // Assert v1
        created.Version.Should().Be(1);
        created.BodyPath.Should().Be($"templates/{created.Id}/v1.docx");

        // Act — update to v2
        using var v2Stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x02 });
        var updated1 = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Multi-version Letter",
            BodyPath = created.BodyPath,
            DocxContent = v2Stream
        });

        // Assert v2
        updated1.Version.Should().Be(2);
        updated1.BodyPath.Should().Be($"templates/{created.Id}/v2.docx");

        // Act — update to v3
        using var v3Stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03 });
        var updated2 = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Multi-version Letter v3",
            BodyPath = updated1.BodyPath,
            DocxContent = v3Stream
        });

        // Assert v3
        updated2.Version.Should().Be(3);
        updated2.BodyPath.Should().Be($"templates/{created.Id}/v3.docx");
    }

    [Fact]
    public async Task Create_ThenUpdate_VersionPathsAreDistinct()
    {
        // Arrange
        using var v1Stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x01 });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Distinct Paths Letter",
            Channel = ChannelType.Letter,
            DocxContent = v1Stream
        });

        // Capture v1 path before update (EF tracking will mutate the entity in-place)
        var v1Path = DocxFilePathHelper.Build(created.Id, 1);

        using var v2Stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x02 });
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Distinct Paths Letter",
            BodyPath = v1Path,
            DocxContent = v2Stream
        });

        // Assert — each version has a unique path
        v1Path.Should().Contain("/v1.docx");
        updated.BodyPath.Should().Contain("/v2.docx");
        v1Path.Should().NotBe(updated.BodyPath);
    }

    [Fact]
    public async Task Create_ThenUpdate_BodyStoreCalledWithCorrectPaths()
    {
        // Arrange — track all WriteAsync calls
        var writtenPaths = new List<string>();
        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((path, _, _) => writtenPaths.Add(path))
            .ReturnsAsync((string p, Stream _, CancellationToken _) => p);

        // Act
        using var v1Stream = new MemoryStream(new byte[] { 0x01 });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Store Path Verification",
            Channel = ChannelType.Letter,
            DocxContent = v1Stream
        });

        using var v2Stream = new MemoryStream(new byte[] { 0x02 });
        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Store Path Verification",
            BodyPath = created.BodyPath,
            DocxContent = v2Stream
        });

        // Assert — two distinct paths were written, v1 then v2
        writtenPaths.Should().HaveCount(2);
        writtenPaths[0].Should().Be($"templates/{created.Id}/v1.docx");
        writtenPaths[1].Should().Be($"templates/{created.Id}/v2.docx");
    }

    [Fact]
    public async Task Create_BodyPath_IsRelativeNotAbsolute()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 0x50, 0x4B });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Relative Path Assertion",
            Channel = ChannelType.Letter,
            DocxContent = stream
        });

        // Assert — path is relative (no drive letter, no leading slash)
        created.BodyPath.Should().NotContain(":\\");
        created.BodyPath.Should().NotStartWith("/");
        created.BodyPath.Should().StartWith("templates/");
    }

    [Fact]
    public async Task HistorySnapshot_PreservesV1Path_AfterUpdate()
    {
        // Arrange — create v1
        using var v1Stream = new MemoryStream(new byte[] { 0x01 });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "History Path Test",
            Channel = ChannelType.Letter,
            DocxContent = v1Stream
        });

        // Capture v1 path before update (EF tracking will mutate the entity in-place)
        var v1Path = DocxFilePathHelper.Build(created.Id, 1);

        // Act — update to v2 (creates a history snapshot of v1)
        using var v2Stream = new MemoryStream(new byte[] { 0x02 });
        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "History Path Test",
            BodyPath = v1Path,
            DocxContent = v2Stream
        });

        // Retrieve history
        var history = await _service.GetHistoryAsync(created.Id);

        // Assert — v1 snapshot preserves the original v1.docx path
        history.Should().HaveCount(1);
        history[0].Version.Should().Be(1);
        history[0].BodyPath.Should().Be(v1Path);
        history[0].BodyPath.Should().Contain("/v1.docx");
    }
}
