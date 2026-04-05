using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Batch;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text.Json;

namespace CampaignEngine.Infrastructure.Tests.Batch;

/// <summary>
/// Integration tests for <see cref="ProcessChunkJob"/> DOCX rendering pipeline.
///
/// TASK-021-06: Integration test for chunk job with a Letter/DOCX template.
/// TASK-021-07: Verify one DOCX file rendered per recipient (BinaryContent set once per recipient,
///              LetterDispatcher.SendAsync called once per recipient).
///
/// Coverage:
///   - Letter channel: DOCX bytes read from ITemplateBodyStore per chunk
///   - IDocxTemplateRenderer.RenderAsync called once per recipient
///   - DispatchRequest.BinaryContent populated with rendered bytes (non-null)
///   - DispatchRequest.Content remains null for Letter channel
///   - ILoggingDispatchOrchestrator.SendWithLoggingAsync called once per recipient
///   - Coordinator notified with correct success count
///   - Missing DOCX body path → chunk failure (not per-recipient failure)
///   - Email channel: Content rendered (existing behaviour not regressed)
/// </summary>
public class ProcessChunkJobDocxTests
{
    // ----------------------------------------------------------------
    // Shared mocks and test data
    // ----------------------------------------------------------------

    private readonly Mock<ICampaignChunkRepository> _chunkRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ITemplateRenderer> _templateRendererMock = new();
    private readonly Mock<IDocxTemplateRenderer> _docxRendererMock = new();
    private readonly Mock<ITemplateBodyStore> _bodyStoreMock = new();
    private readonly Mock<ILoggingDispatchOrchestrator> _orchestratorMock = new();
    private readonly Mock<IChunkCoordinatorService> _coordinatorMock = new();
    private readonly Mock<ICcResolutionService> _ccResolutionMock = new();
    private readonly Mock<IAppLogger<ProcessChunkJob>> _loggerMock = new();

    private static readonly byte[] MinimalDocxBytes = CreateMinimalDocxBytes();

    // ----------------------------------------------------------------
    // Factory
    // ----------------------------------------------------------------

    private ProcessChunkJob CreateSut() => new(
        _chunkRepoMock.Object,
        _unitOfWorkMock.Object,
        _templateRendererMock.Object,
        _docxRendererMock.Object,
        _bodyStoreMock.Object,
        _orchestratorMock.Object,
        _coordinatorMock.Object,
        _ccResolutionMock.Object,
        _loggerMock.Object);

    // ----------------------------------------------------------------
    // Test data helpers
    // ----------------------------------------------------------------

    private static CampaignChunk BuildLetterChunk(
        string? docxBodyPath = "templates/abc/v1.docx",
        int recipientCount = 1,
        string[]? recipientIds = null)
    {
        var ids = recipientIds ?? Enumerable.Range(1, recipientCount).Select(i => $"REC-{i:000}").ToArray();

        var recipients = ids.Select(id => new Dictionary<string, object?>
        {
            ["id"] = id,
            ["firstName"] = "Alice",
            ["lastName"] = "Smith"
        }).ToList();

        var campaignId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();

        var snapshot = new TemplateSnapshot
        {
            Id = snapshotId,
            OriginalTemplateId = Guid.NewGuid(),
            TemplateVersion = 1,
            Name = "Invoice Letter",
            Channel = ChannelType.Letter,
            ResolvedHtmlBody = docxBodyPath ?? string.Empty
        };

        var step = new CampaignStep
        {
            Id = stepId,
            CampaignId = campaignId,
            Channel = ChannelType.Letter,
            TemplateId = Guid.NewGuid(),
            TemplateSnapshotId = snapshotId,
            TemplateSnapshot = snapshot
        };

        return new CampaignChunk
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            CampaignStepId = stepId,
            CampaignStep = step,
            Status = ChunkStatus.Pending,
            RecipientDataJson = JsonSerializer.Serialize(recipients)
        };
    }

    private void SetupDefaultMocks(CampaignChunk chunk, byte[]? renderedBytes = null)
    {
        _chunkRepoMock
            .Setup(r => r.GetWithDetailsAsync(chunk.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Default: return a stream with the minimal DOCX bytes
        _bodyStoreMock
            .Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(MinimalDocxBytes));

        // Default: return some rendered bytes
        var bytes = renderedBytes ?? MinimalDocxBytes;
        _docxRendererMock
            .Setup(r => r.RenderAsync(
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, List<Dictionary<string, string>>>>(),
                It.IsAny<Dictionary<string, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Default: orchestrator returns success
        _orchestratorMock
            .Setup(o => o.SendWithLoggingAsync(
                It.IsAny<DispatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), DispatchResult.Ok("MSG-001")));
    }

    // ----------------------------------------------------------------
    // TASK-021-01: DOCX bytes read from ITemplateBodyStore
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannel_ReadsDocxBytesFromBodyStore()
    {
        // Arrange
        var chunk = BuildLetterChunk(docxBodyPath: "templates/letter/v2.docx");
        SetupDefaultMocks(chunk);
        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert — store was read exactly once (shared across all recipients)
        _bodyStoreMock.Verify(
            s => s.ReadAsync("templates/letter/v2.docx", It.IsAny<CancellationToken>()),
            Times.Once,
            "DOCX template should be read from the store exactly once per chunk");
    }

    // ----------------------------------------------------------------
    // TASK-021-02: IDocxTemplateRenderer.RenderAsync called once per recipient
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannelOneRecipient_RendersDocxOnce()
    {
        // Arrange
        var chunk = BuildLetterChunk(recipientCount: 1);
        SetupDefaultMocks(chunk);
        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        _docxRendererMock.Verify(
            r => r.RenderAsync(
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, List<Dictionary<string, string>>>>(),
                It.IsAny<Dictionary<string, bool>>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "RenderAsync should be called once per recipient");
    }

    [Fact]
    public async Task ExecuteAsync_LetterChannelThreeRecipients_RendersDocxThreeTimes()
    {
        // Arrange — TASK-021-07: one DOCX per recipient
        var chunk = BuildLetterChunk(recipientCount: 3);
        SetupDefaultMocks(chunk);
        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        _docxRendererMock.Verify(
            r => r.RenderAsync(
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, List<Dictionary<string, string>>>>(),
                It.IsAny<Dictionary<string, bool>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "RenderAsync must be called once per recipient (no batching)");
    }

    // ----------------------------------------------------------------
    // TASK-021-03: DispatchRequest.BinaryContent set; Content null
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannel_SetsBinaryContentOnDispatchRequest()
    {
        // Arrange
        var renderedBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x01, 0x02 };
        var chunk = BuildLetterChunk(recipientCount: 1);
        SetupDefaultMocks(chunk, renderedBytes: renderedBytes);

        DispatchRequest? capturedRequest = null;
        _orchestratorMock
            .Setup(o => o.SendWithLoggingAsync(
                It.IsAny<DispatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<DispatchRequest, string?, int, CancellationToken>(
                (req, _, _, _) => capturedRequest = req)
            .ReturnsAsync((Guid.NewGuid(), DispatchResult.Ok("MSG")));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.BinaryContent.Should().NotBeNull("BinaryContent must carry the rendered DOCX bytes");
        capturedRequest.BinaryContent.Should().Equal(renderedBytes, "the rendered bytes must be forwarded intact");
        capturedRequest.Content.Should().BeNull("Content must be null for Letter channel (mutual exclusivity)");
    }

    [Fact]
    public async Task ExecuteAsync_LetterChannel_DispatchRequestChannelIsLetter()
    {
        // Arrange
        var chunk = BuildLetterChunk(recipientCount: 1);
        SetupDefaultMocks(chunk);

        DispatchRequest? capturedRequest = null;
        _orchestratorMock
            .Setup(o => o.SendWithLoggingAsync(
                It.IsAny<DispatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<DispatchRequest, string?, int, CancellationToken>(
                (req, _, _, _) => capturedRequest = req)
            .ReturnsAsync((Guid.NewGuid(), DispatchResult.Ok("MSG")));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        capturedRequest!.Channel.Should().Be(ChannelType.Letter);
    }

    // ----------------------------------------------------------------
    // TASK-021-04: LetterDispatcher.SendAsync called once per recipient
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannelThreeRecipients_CallsSendOncePerRecipient()
    {
        // Arrange — TASK-021-07
        var chunk = BuildLetterChunk(recipientCount: 3);
        SetupDefaultMocks(chunk);
        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        _orchestratorMock.Verify(
            o => o.SendWithLoggingAsync(
                It.IsAny<DispatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "SendAsync must be called once per recipient (no batching)");
    }

    [Fact]
    public async Task ExecuteAsync_LetterChannelThreeRecipients_ReportsThreeSuccesses()
    {
        // Arrange
        var chunk = BuildLetterChunk(recipientCount: 3);
        SetupDefaultMocks(chunk);
        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        _coordinatorMock.Verify(
            c => c.RecordChunkCompletionAsync(
                chunk.Id,
                3,   // successCount
                0,   // failureCount
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Scalar extraction: recipient fields passed to renderer
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannel_PassesRecipientScalarsToRenderer()
    {
        // Arrange — recipient has firstName / lastName fields
        var recipients = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = "REC-001", ["firstName"] = "Alice", ["lastName"] = "Smith" }
        };
        var chunk = BuildLetterChunk();
        chunk.RecipientDataJson = JsonSerializer.Serialize(recipients);
        SetupDefaultMocks(chunk);

        Dictionary<string, string>? capturedScalars = null;
        _docxRendererMock
            .Setup(r => r.RenderAsync(
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, List<Dictionary<string, string>>>>(),
                It.IsAny<Dictionary<string, bool>>(),
                It.IsAny<CancellationToken>()))
            .Callback<byte[], Dictionary<string, string>,
                Dictionary<string, List<Dictionary<string, string>>>,
                Dictionary<string, bool>, CancellationToken>(
                (_, scalars, _, _, _) => capturedScalars = scalars)
            .ReturnsAsync(MinimalDocxBytes);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        capturedScalars.Should().NotBeNull();
        capturedScalars.Should().ContainKey("firstName");
        capturedScalars.Should().ContainKey("lastName");
    }

    // ----------------------------------------------------------------
    // TASK-021-05: No PDF accumulation — Content is null for Letter channel
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannel_DoesNotSetContentOnDispatchRequest()
    {
        // Arrange
        var chunk = BuildLetterChunk(recipientCount: 2);
        SetupDefaultMocks(chunk);

        var capturedRequests = new List<DispatchRequest>();
        _orchestratorMock
            .Setup(o => o.SendWithLoggingAsync(
                It.IsAny<DispatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<DispatchRequest, string?, int, CancellationToken>(
                (req, _, _, _) => capturedRequests.Add(req))
            .ReturnsAsync((Guid.NewGuid(), DispatchResult.Ok("MSG")));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert — two recipients, two requests, none have Content set
        capturedRequests.Should().HaveCount(2);
        capturedRequests.Should().AllSatisfy(req =>
            req.Content.Should().BeNull("Letter channel must use BinaryContent, not Content"));
    }

    // ----------------------------------------------------------------
    // Error case: missing DOCX body path
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannel_EmptyBodyPath_ReportsChunkFailure()
    {
        // Arrange — snapshot has no body path
        var chunk = BuildLetterChunk(docxBodyPath: "");
        SetupDefaultMocks(chunk);
        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert — chunk failure reported, no renders attempted
        _coordinatorMock.Verify(
            c => c.RecordChunkFailureAsync(
                chunk.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "chunk should fail when DOCX body path is missing");

        _docxRendererMock.Verify(
            r => r.RenderAsync(
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, List<Dictionary<string, string>>>>(),
                It.IsAny<Dictionary<string, bool>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "renderer should not be called when DOCX body path is missing");
    }

    // ----------------------------------------------------------------
    // Error case: store read failure
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_LetterChannel_StoreReadFailure_ReportsChunkFailure()
    {
        // Arrange
        var chunk = BuildLetterChunk();
        SetupDefaultMocks(chunk);

        _bodyStoreMock
            .Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk read error"));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(chunk.Id, CancellationToken.None);

        // Assert
        _coordinatorMock.Verify(
            c => c.RecordChunkFailureAsync(
                chunk.Id,
                It.Is<string>(msg => msg.Contains("read DOCX template")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Non-regression: Email channel still uses Content (not BinaryContent)
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_EmailChannel_UsesContentNotBinaryContent()
    {
        // Arrange — email channel chunk
        var campaignId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();

        var snapshot = new TemplateSnapshot
        {
            Id = snapshotId,
            OriginalTemplateId = Guid.NewGuid(),
            TemplateVersion = 1,
            Name = "Welcome Email",
            Channel = ChannelType.Email,
            ResolvedHtmlBody = "<html>Hello {{ firstName }}</html>"
        };

        var step = new CampaignStep
        {
            Id = stepId,
            CampaignId = campaignId,
            Channel = ChannelType.Email,
            TemplateId = Guid.NewGuid(),
            TemplateSnapshotId = snapshotId,
            TemplateSnapshot = snapshot
        };

        var recipients = new List<Dictionary<string, object?>>
        {
            new() { ["email"] = "alice@example.com", ["firstName"] = "Alice" }
        };

        var emailChunk = new CampaignChunk
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            CampaignStepId = stepId,
            CampaignStep = step,
            Status = ChunkStatus.Pending,
            RecipientDataJson = JsonSerializer.Serialize(recipients)
        };

        _chunkRepoMock
            .Setup(r => r.GetWithDetailsAsync(emailChunk.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emailChunk);

        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _templateRendererMock
            .Setup(r => r.RenderAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html>Hello Alice</html>");

        _ccResolutionMock
            .Setup(s => s.ResolveCc(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<IDictionary<string, object?>>()))
            .Returns([]);

        _ccResolutionMock
            .Setup(s => s.ResolveBcc(It.IsAny<string?>()))
            .Returns([]);

        DispatchRequest? capturedRequest = null;
        _orchestratorMock
            .Setup(o => o.SendWithLoggingAsync(
                It.IsAny<DispatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<DispatchRequest, string?, int, CancellationToken>(
                (req, _, _, _) => capturedRequest = req)
            .ReturnsAsync((Guid.NewGuid(), DispatchResult.Ok("MSG")));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(emailChunk.Id, CancellationToken.None);

        // Assert — email channel uses Content, not BinaryContent
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Content.Should().NotBeNullOrEmpty("Email channel must use Content");
        capturedRequest.BinaryContent.Should().BeNull("Email channel must not set BinaryContent");

        // DOCX store and renderer should not be called for Email channel
        _bodyStoreMock.Verify(
            s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "ITemplateBodyStore should not be used for Email channel");

        _docxRendererMock.Verify(
            r => r.RenderAsync(
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, List<Dictionary<string, string>>>>(),
                It.IsAny<Dictionary<string, bool>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "IDocxTemplateRenderer should not be called for Email channel");
    }

    // ----------------------------------------------------------------
    // Helper: create minimal valid DOCX bytes in memory
    // ----------------------------------------------------------------

    private static byte[] CreateMinimalDocxBytes()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(
                    new Paragraph(
                        new Run(new Text("Hello {{ firstName }}")))));
        }
        return ms.ToArray();
    }
}
