using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using CampaignEngine.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Templates;

/// <summary>
/// US-005 TASK-005-08: Integration tests for update with history copy.
///
/// Tests the full create → update cycle to verify that:
/// - On update with a new DOCX file, the previous body is copied to history/v{n}.docx.
/// - On update with a new HTML file, the previous body is copied to history/v{n}.html.
/// - CopyAsync is called with the correct source and destination paths.
/// - History snapshot BodyPath is preserved (from US-008 base).
/// </summary>
public class TemplateAtomicWriteIntegrationTests : DbContextTestBase
{
    private readonly TemplateService _service;
    private readonly Mock<ITemplateBodyStore> _bodyStoreMock;

    public TemplateAtomicWriteIntegrationTests()
    {
        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        _bodyStoreMock = new Mock<ITemplateBodyStore>();

        // Default: WriteAsync returns path unchanged
        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        // Default: CopyAsync is a no-op (will capture call arguments in individual tests)
        _bodyStoreMock
            .Setup(s => s.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var templateRepository = new TemplateRepository(Context);
        var unitOfWork = new UnitOfWork(Context);

        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object,
            manifestService.Object, parserService.Object,
            _bodyStoreMock.Object);
    }

    // ----------------------------------------------------------------
    // DOCX: update copies previous body to history/v{n}.docx
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WithDocxContent_CopiesPreviousBodyToHistory()
    {
        // Arrange — create v1
        using var v1Stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x01 });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "History Copy Test",
            Channel = ChannelType.Letter,
            DocxContent = v1Stream
        });

        var originalBodyPath = created.BodyPath;
        originalBodyPath.Should().Contain("v1.docx");

        // Capture CopyAsync calls for v2 update
        string? capturedCopySource = null;
        string? capturedCopyDest = null;
        _bodyStoreMock
            .Setup(s => s.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((src, dest, _) =>
            {
                capturedCopySource = src;
                capturedCopyDest = dest;
            })
            .Returns(Task.CompletedTask);

        // Act — update with new DOCX content
        using var v2Stream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x02 });
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "History Copy Test",
            DocxContent = v2Stream
        });

        // Assert — CopyAsync was called once
        _bodyStoreMock.Verify(
            s => s.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — source is the original v1 path
        capturedCopySource.Should().Be(originalBodyPath);

        // Assert — destination is the history path: templates/{id}/history/v1.docx
        capturedCopyDest.Should().NotBeNull();
        capturedCopyDest.Should().Contain("history");
        capturedCopyDest.Should().Contain("v1.docx");
        capturedCopyDest.Should().Contain(created.Id.ToString());

        // Assert — the template now points to v2
        updated.BodyPath.Should().Contain("v2.docx");
        updated.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_WithHtmlContent_CopiesPreviousBodyToHistory()
    {
        // Arrange — create v1 HTML template
        using var v1Stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<html>v1</html>"));
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Html History Test",
            Channel = ChannelType.Email,
            HtmlContent = v1Stream
        });

        var originalBodyPath = created.BodyPath;
        originalBodyPath.Should().Contain("v1.html");

        // Capture CopyAsync calls
        string? capturedCopyDest = null;
        _bodyStoreMock
            .Setup(s => s.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, dest, _) => capturedCopyDest = dest)
            .Returns(Task.CompletedTask);

        // Act — update with new HTML content
        using var v2Stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<html>v2</html>"));
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Html History Test",
            HtmlContent = v2Stream
        });

        // Assert — history path uses .html extension
        capturedCopyDest.Should().NotBeNull();
        capturedCopyDest.Should().Contain("history");
        capturedCopyDest.Should().Contain("v1.html");

        // Assert — new body path is v2.html
        updated.BodyPath.Should().Contain("v2.html");
        updated.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_WithNoNewFile_DoesNotCopyToHistory()
    {
        // Arrange — create a template with no file stream (metadata-only)
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Metadata Only",
            Channel = ChannelType.Email,
            BodyPath = "templates/metadata-only/v1.html"
        });

        // Act — update only metadata, no new file
        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Metadata Only Renamed",
            BodyPath = "templates/metadata-only/v1.html"
        });

        // Assert — CopyAsync was never called (no file update)
        _bodyStoreMock.Verify(
            s => s.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_MultipleUpdates_EachVersionCopiedToHistory()
    {
        // Arrange — create v1
        using var v1Stream = new MemoryStream(new byte[] { 0x01 });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Multi-History Test",
            Channel = ChannelType.Letter,
            DocxContent = v1Stream
        });

        var historyCopies = new List<(string Source, string Dest)>();
        _bodyStoreMock
            .Setup(s => s.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((src, dest, _) =>
                historyCopies.Add((src, dest)))
            .Returns(Task.CompletedTask);

        // Act — update to v2
        using var v2Stream = new MemoryStream(new byte[] { 0x02 });
        var v2 = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Multi-History Test",
            DocxContent = v2Stream
        });

        // Act — update to v3
        using var v3Stream = new MemoryStream(new byte[] { 0x03 });
        var v3 = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Multi-History Test",
            DocxContent = v3Stream
        });

        // Assert — two history copies were made
        historyCopies.Should().HaveCount(2);

        // First copy: v1 → history/v1.docx
        historyCopies[0].Source.Should().Contain("v1.docx");
        historyCopies[0].Dest.Should().Contain("history");
        historyCopies[0].Dest.Should().Contain("v1.docx");

        // Second copy: v2 → history/v2.docx
        historyCopies[1].Source.Should().Contain("v2.docx");
        historyCopies[1].Dest.Should().Contain("history");
        historyCopies[1].Dest.Should().Contain("v2.docx");

        // Template is now at version 3
        v3.Version.Should().Be(3);
    }

    // ----------------------------------------------------------------
    // TASK-005-08: History copy DB round-trip — snapshot BodyPath preserved
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WithDocxContent_HistorySnapshotRecordsOriginalBodyPath()
    {
        // Arrange — create v1
        using var v1Stream = new MemoryStream(new byte[] { 0x50, 0x4B });
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Snapshot BodyPath Test",
            Channel = ChannelType.Letter,
            DocxContent = v1Stream
        });

        var originalBodyPath = created.BodyPath;

        // Act — update to v2
        using var v2Stream = new MemoryStream(new byte[] { 0x50, 0x4C });
        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Snapshot BodyPath Test",
            DocxContent = v2Stream
        });

        // Assert — history entry in DB has original v1 path
        var historyEntries = await Context.Set<CampaignEngine.Domain.Entities.TemplateHistory>()
            .Where(h => h.TemplateId == created.Id && h.Version == 1)
            .ToListAsync();

        historyEntries.Should().HaveCount(1);
        historyEntries[0].BodyPath.Should().Be(originalBodyPath);
        historyEntries[0].Version.Should().Be(1);
    }
}
