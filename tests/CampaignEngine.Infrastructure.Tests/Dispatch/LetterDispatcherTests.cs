using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Unit tests for LetterDispatcher.
///
/// TASK-021-06: PDF generation tests.
/// TASK-021-07: Consolidation tests (verify page order and batch splitting).
///
/// Uses:
/// - MockLetterPostProcessorRegistry — returns configurable PDF bytes or throws.
/// - MockPdfConsolidationService    — wraps PdfConsolidationService for integration-style tests.
/// - NoOpFileDropHandler            — captures written batches without touching the file system.
/// </summary>
public class LetterDispatcherTests
{
    // ----------------------------------------------------------------
    // Default options
    // ----------------------------------------------------------------

    private static readonly LetterOptions DefaultOptions = new()
    {
        IsEnabled = true,
        OutputDirectory = "/tmp/letters-test",
        MaxPagesPerBatch = 500,
        GenerateManifest = true,
        FileNamePrefix = "CAMPAIGN"
    };

    // ----------------------------------------------------------------
    // Factory helpers
    // ----------------------------------------------------------------

    private static LetterDispatcher CreateDispatcher(
        MockLetterPostProcessorRegistry? registry = null,
        MockPdfConsolidationService? consolidation = null,
        NoOpFileDropHandler? fileDropHandler = null,
        LetterOptions? options = null)
    {
        var reg = registry ?? new MockLetterPostProcessorRegistry(
            PostProcessingResult.Binary(CreateMinimalPdf(1)));
        var cons = consolidation ?? new MockPdfConsolidationService(500);
        var drop = fileDropHandler ?? new NoOpFileDropHandler(options ?? DefaultOptions);
        var opts = Options.Create(options ?? DefaultOptions);

        return new LetterDispatcher(
            reg,
            cons,
            drop,
            opts,
            NullLogger<LetterDispatcher>.Instance);
    }

    private static DispatchRequest BuildRequest(
        string? recipientId = null,
        string? displayName = null,
        string? content = null,
        Guid? campaignId = null) => new()
    {
        Channel = ChannelType.Letter,
        Content = content ?? "<html><body><p>Dear {{ Name }}, your letter.</p></body></html>",
        CampaignId = campaignId ?? Guid.NewGuid(),
        Recipient = new RecipientInfo
        {
            ExternalRef = recipientId ?? "REC-001",
            DisplayName = displayName ?? "Alice Smith",
            Email = "alice@example.com"
        }
    };

    // ----------------------------------------------------------------
    // IChannelDispatcher contract
    // ----------------------------------------------------------------

    [Fact]
    public void Channel_ReturnsLetter()
    {
        CreateDispatcher().Channel.Should().Be(ChannelType.Letter);
    }

    // ----------------------------------------------------------------
    // TASK-021-06: PDF generation — happy path
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ValidRequest_ReturnsSuccess()
    {
        var dispatcher = CreateDispatcher();
        var result = await dispatcher.SendAsync(BuildRequest());

        result.Success.Should().BeTrue();
        result.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ValidRequest_MessageIdContainsCampaignId()
    {
        var campaignId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();
        var result = await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));

        result.Success.Should().BeTrue();
        result.MessageId.Should().Contain(campaignId.ToString("N"));
    }

    [Fact]
    public async Task SendAsync_ValidRequest_AccumulatesOneEntry()
    {
        var dispatcher = CreateDispatcher();
        await dispatcher.SendAsync(BuildRequest());

        dispatcher.AccumulatedCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_ThreeRecipients_AccumulatesThreeEntries()
    {
        var dispatcher = CreateDispatcher();
        var campaignId = Guid.NewGuid();

        for (var i = 1; i <= 3; i++)
        {
            await dispatcher.SendAsync(BuildRequest(recipientId: $"REC-{i:D3}", campaignId: campaignId));
        }

        dispatcher.AccumulatedCount.Should().Be(3);
    }

    // ----------------------------------------------------------------
    // TASK-021-06: PDF generation — channel disabled
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ChannelDisabled_ReturnsPermanentFailure()
    {
        var opts = DefaultOptions with { IsEnabled = false };
        var dispatcher = CreateDispatcher(options: opts);

        var result = await dispatcher.SendAsync(BuildRequest());

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("disabled");
    }

    // ----------------------------------------------------------------
    // TASK-021-06: PDF generation — empty content
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_EmptyContent_ReturnsPermanentFailure()
    {
        var dispatcher = CreateDispatcher();
        var result = await dispatcher.SendAsync(BuildRequest(content: string.Empty));

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("empty");
    }

    [Fact]
    public async Task SendAsync_WhitespaceContent_ReturnsPermanentFailure()
    {
        var dispatcher = CreateDispatcher();
        var result = await dispatcher.SendAsync(BuildRequest(content: "   "));

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    // ----------------------------------------------------------------
    // TASK-021-06: PDF generation — post-processor error handling
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_PostProcessorThrowsTransient_ReturnsTransientFailure()
    {
        var registry = new MockLetterPostProcessorRegistry(
            exception: new PostProcessingException("DinkToPdf timeout", channel: "Letter", isTransient: true));
        var dispatcher = CreateDispatcher(registry: registry);

        var result = await dispatcher.SendAsync(BuildRequest());

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_PostProcessorThrowsPermanent_ReturnsPermanentFailure()
    {
        var registry = new MockLetterPostProcessorRegistry(
            exception: new PostProcessingException("Invalid HTML", channel: "Letter", isTransient: false));
        var dispatcher = CreateDispatcher(registry: registry);

        var result = await dispatcher.SendAsync(BuildRequest());

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_PostProcessorReturnsEmptyBytes_ReturnsTransientFailure()
    {
        var registry = new MockLetterPostProcessorRegistry(
            PostProcessingResult.Binary(Array.Empty<byte>()));
        var dispatcher = CreateDispatcher(registry: registry);

        var result = await dispatcher.SendAsync(BuildRequest());

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_CancellationRequested_ReturnsTransientFailure()
    {
        var cts = new CancellationTokenSource();
        var registry = new MockLetterPostProcessorRegistry(
            exception: new OperationCanceledException("Cancelled"));
        var dispatcher = CreateDispatcher(registry: registry);

        var result = await dispatcher.SendAsync(BuildRequest(), cts.Token);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // TASK-021-07: Consolidation — page order verification
    // ----------------------------------------------------------------

    [Fact]
    public async Task FlushBatchAsync_NoAccumulatedPdfs_ReturnsEmptyList()
    {
        var campaignId = Guid.NewGuid();
        var fileDropHandler = new NoOpFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);

        var paths = await dispatcher.FlushBatchAsync(campaignId);

        paths.Should().BeEmpty();
        fileDropHandler.WrittenBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task FlushBatchAsync_SingleRecipient_WritesOneBatch()
    {
        var campaignId = Guid.NewGuid();
        var fileDropHandler = new NoOpFileDropHandler(DefaultOptions);

        var pdfBytes = CreateMinimalPdf(1);
        var registry = new MockLetterPostProcessorRegistry(PostProcessingResult.Binary(pdfBytes));
        var consolidation = new MockPdfConsolidationService(500);
        var dispatcher = CreateDispatcher(registry, consolidation, fileDropHandler);

        await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));
        var paths = await dispatcher.FlushBatchAsync(campaignId);

        paths.Should().HaveCount(1);
        fileDropHandler.WrittenBatches.Should().HaveCount(1);
    }

    [Fact]
    public async Task FlushBatchAsync_AfterFlush_AccumulatorCleared()
    {
        var campaignId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();

        await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));
        await dispatcher.FlushBatchAsync(campaignId);

        dispatcher.AccumulatedCount.Should().Be(0);
    }

    [Fact]
    public async Task FlushBatchAsync_BatchFileNameContainsCampaignId()
    {
        var campaignId = Guid.NewGuid();
        var fileDropHandler = new NoOpFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);

        await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));
        var paths = await dispatcher.FlushBatchAsync(campaignId);

        paths.Should().HaveCount(1);
        // The file name (passed as written path) should contain campaign ID
        fileDropHandler.WrittenBatches.First().CampaignId.Should().Be(campaignId);
    }

    [Fact]
    public async Task FlushBatchAsync_ManifestCsvGenerated_WhenEnabled()
    {
        var campaignId = Guid.NewGuid();
        var options = DefaultOptions with { GenerateManifest = true };
        var fileDropHandler = new NoOpFileDropHandler(options);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler, options: options);

        await dispatcher.SendAsync(BuildRequest(recipientId: "REC-001", campaignId: campaignId));
        await dispatcher.FlushBatchAsync(campaignId);

        fileDropHandler.WrittenBatches.First().ManifestCsv.Should().NotBeNullOrEmpty();
        fileDropHandler.WrittenBatches.First().ManifestCsv.Should().Contain("REC-001");
    }

    [Fact]
    public async Task FlushBatchAsync_ManifestNotGenerated_WhenDisabled()
    {
        var campaignId = Guid.NewGuid();
        var options = DefaultOptions with { GenerateManifest = false };
        var fileDropHandler = new NoOpFileDropHandler(options);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler, options: options);

        await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));
        await dispatcher.FlushBatchAsync(campaignId);

        fileDropHandler.WrittenBatches.First().ManifestCsv.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // TASK-021-07: Consolidation — page order (insertion order preserved)
    // ----------------------------------------------------------------

    [Fact]
    public async Task FlushBatchAsync_MultipleRecipients_ManifestPreservesOrder()
    {
        var campaignId = Guid.NewGuid();
        var capturer = new ManifestCapturingFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(
            fileDropHandler: capturer,
            options: DefaultOptions);

        for (var i = 1; i <= 3; i++)
        {
            await dispatcher.SendAsync(BuildRequest(
                recipientId: $"REC-{i:D3}",
                campaignId: campaignId));
        }

        await dispatcher.FlushBatchAsync(campaignId);

        var csv = capturer.LastManifestCsv;
        csv.Should().NotBeNull();

        // Verify recipient IDs appear in order
        var indexRec001 = csv!.IndexOf("REC-001", StringComparison.Ordinal);
        var indexRec002 = csv.IndexOf("REC-002", StringComparison.Ordinal);
        var indexRec003 = csv.IndexOf("REC-003", StringComparison.Ordinal);

        indexRec001.Should().BeLessThan(indexRec002, "REC-001 should appear before REC-002 in manifest");
        indexRec002.Should().BeLessThan(indexRec003, "REC-002 should appear before REC-003 in manifest");
    }

    [Fact]
    public async Task FlushBatchAsync_ManifestContainsDisplayName()
    {
        var campaignId = Guid.NewGuid();
        var capturer = new ManifestCapturingFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: capturer, options: DefaultOptions);

        await dispatcher.SendAsync(BuildRequest(
            recipientId: "REC-001",
            displayName: "Jean-Pierre Dupont",
            campaignId: campaignId));
        await dispatcher.FlushBatchAsync(campaignId);

        capturer.LastManifestCsv.Should().Contain("Jean-Pierre Dupont");
    }

    [Fact]
    public async Task FlushBatchAsync_ManifestHasHeader()
    {
        var campaignId = Guid.NewGuid();
        var capturer = new ManifestCapturingFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: capturer, options: DefaultOptions);

        await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));
        await dispatcher.FlushBatchAsync(campaignId);

        capturer.LastManifestCsv.Should().StartWith("SequenceInBatch");
    }

    // ----------------------------------------------------------------
    // TASK-021-07: Consolidation — batch splitting
    // ----------------------------------------------------------------

    [Fact]
    public async Task FlushBatchAsync_10Recipients_MergedIntoSingleBatch()
    {
        var campaignId = Guid.NewGuid();
        var fileDropHandler = new NoOpFileDropHandler(DefaultOptions);
        var pdfBytes = CreateMinimalPdf(1); // 1 page per recipient
        var registry = new MockLetterPostProcessorRegistry(PostProcessingResult.Binary(pdfBytes));
        var consolidation = new MockPdfConsolidationService(500);
        var dispatcher = CreateDispatcher(registry, consolidation, fileDropHandler);

        for (var i = 1; i <= 10; i++)
        {
            await dispatcher.SendAsync(BuildRequest(recipientId: $"REC-{i:D3}", campaignId: campaignId));
        }

        var paths = await dispatcher.FlushBatchAsync(campaignId);

        // 10 pages < 500 max → single batch
        paths.Should().HaveCount(1);
    }

    // ----------------------------------------------------------------
    // Manifest generator unit tests (TASK-021-05)
    // ----------------------------------------------------------------

    [Fact]
    public void ManifestGenerator_EmptyEntries_ReturnsNull()
    {
        var result = LetterManifestGenerator.BuildCsv([]);
        result.Should().BeNull();
    }

    [Fact]
    public void ManifestGenerator_SingleEntry_ContainsHeader()
    {
        var entries = new List<LetterManifestEntry>
        {
            new("REC-001", "Alice", 1, 2, "CAMPAIGN_batch_001.pdf")
        };

        var csv = LetterManifestGenerator.BuildCsv(entries);

        csv.Should().StartWith("SequenceInBatch,RecipientId,DisplayName,PageCount,BatchFileName");
    }

    [Fact]
    public void ManifestGenerator_SingleEntry_ContainsRecipientData()
    {
        var entries = new List<LetterManifestEntry>
        {
            new("REC-001", "Alice Smith", 1, 2, "CAMPAIGN_001.pdf")
        };

        var csv = LetterManifestGenerator.BuildCsv(entries);

        csv.Should().Contain("REC-001");
        csv.Should().Contain("Alice Smith");
        csv.Should().Contain("CAMPAIGN_001.pdf");
    }

    [Fact]
    public void ManifestGenerator_FieldWithComma_EscapedWithQuotes()
    {
        var entries = new List<LetterManifestEntry>
        {
            new("REC-001", "Smith, Alice", 1, 1, "batch.pdf")
        };

        var csv = LetterManifestGenerator.BuildCsv(entries);

        csv.Should().Contain("\"Smith, Alice\"");
    }

    [Fact]
    public void ManifestGenerator_FieldWithQuote_EscapedByDoubling()
    {
        var entries = new List<LetterManifestEntry>
        {
            new("REC-001", "O\"Brien", 1, 1, "batch.pdf")
        };

        var csv = LetterManifestGenerator.BuildCsv(entries);

        csv.Should().Contain("\"O\"\"Brien\"");
    }

    [Fact]
    public void ManifestGenerator_MultipleEntries_AllIncluded()
    {
        var entries = new List<LetterManifestEntry>
        {
            new("REC-001", "Alice", 1, 1, "batch.pdf"),
            new("REC-002", "Bob", 2, 2, "batch.pdf"),
            new("REC-003", "Charlie", 3, 1, "batch.pdf"),
        };

        var csv = LetterManifestGenerator.BuildCsv(entries);

        csv.Should().Contain("REC-001");
        csv.Should().Contain("REC-002");
        csv.Should().Contain("REC-003");
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static byte[] CreateMinimalPdf(int pageCount = 1)
    {
        var doc = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
            doc.AddPage();

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        doc.Dispose();
        return ms.ToArray();
    }
}

// ================================================================
// Test doubles
// ================================================================

/// <summary>
/// Mock IChannelPostProcessorRegistry that returns a configured Letter post-processor.
/// Can be configured to return a result or throw an exception.
/// </summary>
internal class MockLetterPostProcessorRegistry : IChannelPostProcessorRegistry
{
    private readonly PostProcessingResult? _result;
    private readonly Exception? _exception;

    public MockLetterPostProcessorRegistry(PostProcessingResult result)
    {
        _result = result;
    }

    public MockLetterPostProcessorRegistry(Exception exception)
    {
        _exception = exception;
    }

    public IChannelPostProcessor GetProcessor(ChannelType channel)
    {
        return new MockLetterPostProcessor(_result, _exception);
    }
}

internal sealed class MockLetterPostProcessor : IChannelPostProcessor
{
    private readonly PostProcessingResult? _result;
    private readonly Exception? _exception;

    public ChannelType Channel => ChannelType.Letter;

    public MockLetterPostProcessor(PostProcessingResult? result, Exception? exception)
    {
        _result = result;
        _exception = exception;
    }

    public Task<PostProcessingResult> ProcessAsync(
        string renderedHtml,
        PostProcessingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (_exception is not null)
            throw _exception;

        return Task.FromResult(_result!);
    }
}

/// <summary>
/// Mock IPdfConsolidationService that uses PdfSharp for realistic consolidation
/// (supports page count verification in tests).
/// </summary>
internal sealed class MockPdfConsolidationService : IPdfConsolidationService
{
    public int MaxPagesPerBatch { get; }

    public MockPdfConsolidationService(int maxPagesPerBatch = 500)
    {
        MaxPagesPerBatch = maxPagesPerBatch;
    }

    public Task<IReadOnlyList<byte[]>> ConsolidateAsync(
        IEnumerable<byte[]> pdfDocuments,
        CancellationToken cancellationToken = default)
    {
        var docs = pdfDocuments?.ToList() ?? [];

        if (docs.Count == 0)
            return Task.FromResult<IReadOnlyList<byte[]>>(Array.Empty<byte[]>());

        // Simple pass-through: merge all into a single batch
        // (for realistic batching tests, use PdfConsolidationService directly)
        var batches = new List<byte[]>();
        var current = new PdfDocument();
        var pageCount = 0;

        foreach (var pdfBytes in docs)
        {
            if (pdfBytes is null || pdfBytes.Length == 0) continue;

            PdfDocument? source = null;
            try
            {
                var ms = new MemoryStream(pdfBytes);
                source = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            }
            catch
            {
                continue;
            }

            using (source)
            {
                for (var i = 0; i < source.PageCount; i++)
                {
                    if (pageCount >= MaxPagesPerBatch)
                    {
                        batches.Add(SaveDoc(current));
                        current = new PdfDocument();
                        pageCount = 0;
                    }

                    current.AddPage(source.Pages[i]);
                    pageCount++;
                }
            }
        }

        if (pageCount > 0)
            batches.Add(SaveDoc(current));

        current.Dispose();

        return Task.FromResult<IReadOnlyList<byte[]>>(batches);
    }

    private static byte[] SaveDoc(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }
}

/// <summary>
/// File drop handler that captures written batches in memory instead of writing to disk.
/// </summary>
internal sealed class NoOpFileDropHandler : PrintProviderFileDropHandler
{
    public record WrittenBatch(Guid CampaignId, int BatchNumber, byte[] PdfBytes, string? ManifestCsv);
    public List<WrittenBatch> WrittenBatches { get; } = [];

    public NoOpFileDropHandler(LetterOptions options)
        : base(Options.Create(options), NullLogger<PrintProviderFileDropHandler>.Instance)
    { }

    public override Task<string> WriteAsync(
        byte[] pdfBytes,
        string? manifestCsv,
        Guid campaignId,
        int batchNumber,
        DateTime? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        WrittenBatches.Add(new WrittenBatch(campaignId, batchNumber, pdfBytes, manifestCsv));
        var fakePath = Path.Combine("test-output", $"CAMPAIGN_{campaignId:N}_{batchNumber:D3}.pdf");
        return Task.FromResult(fakePath);
    }
}

/// <summary>
/// File drop handler that captures the last manifest CSV for assertion.
/// </summary>
internal sealed class ManifestCapturingFileDropHandler : PrintProviderFileDropHandler
{
    public string? LastManifestCsv { get; private set; }

    public ManifestCapturingFileDropHandler(LetterOptions options)
        : base(Options.Create(options), NullLogger<PrintProviderFileDropHandler>.Instance)
    { }

    public override Task<string> WriteAsync(
        byte[] pdfBytes,
        string? manifestCsv,
        Guid campaignId,
        int batchNumber,
        DateTime? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        LastManifestCsv = manifestCsv;
        return Task.FromResult($"CAMPAIGN_{campaignId:N}_{batchNumber:D3}.pdf");
    }
}
