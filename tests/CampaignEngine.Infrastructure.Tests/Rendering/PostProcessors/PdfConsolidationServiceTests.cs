using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Rendering.PostProcessors;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CampaignEngine.Infrastructure.Tests.Rendering.PostProcessors;

/// <summary>
/// Unit tests for PdfConsolidationService.
/// Covers: empty input, single document, multi-document consolidation,
/// page-count batching (BR-4: 500 pages max), invalid PDF handling, cancellation.
///
/// TASK-013-05 + TASK-013-08 (performance).
/// </summary>
public class PdfConsolidationServiceTests
{
    private readonly PdfConsolidationService _service;

    public PdfConsolidationServiceTests()
    {
        _service = new PdfConsolidationService(NullLogger<PdfConsolidationService>.Instance);
    }

    // ----------------------------------------------------------------
    // Helper: create a minimal valid 1-page PDF in memory
    // ----------------------------------------------------------------

    private static byte[] CreateMinimalPdf(int pageCount = 1)
    {
        var doc = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            doc.AddPage();
        }

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        doc.Dispose();
        return ms.ToArray();
    }

    // ----------------------------------------------------------------
    // Constants
    // ----------------------------------------------------------------

    [Fact]
    public void MaxPagesPerBatch_Is500()
    {
        _service.MaxPagesPerBatch.Should().Be(500);
        PdfConsolidationService.DefaultMaxPagesPerBatch.Should().Be(500);
    }

    // ----------------------------------------------------------------
    // Edge cases: empty input
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConsolidateAsync_EmptyCollection_ReturnsEmptyList()
    {
        var result = await _service.ConsolidateAsync(Array.Empty<byte[]>());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsolidateAsync_NullCollection_ThrowsArgumentNullException()
    {
        var act = async () => await _service.ConsolidateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ----------------------------------------------------------------
    // Single document
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConsolidateAsync_SingleOnePage_ReturnsSingleBatch()
    {
        var pdf = CreateMinimalPdf(1);

        var result = await _service.ConsolidateAsync(new[] { pdf });

        result.Should().HaveCount(1);
        result[0].Should().NotBeEmpty();
    }

    [Fact]
    public async Task ConsolidateAsync_SingleDocument_OutputIsValidPdf()
    {
        var pdf = CreateMinimalPdf(3);

        var result = await _service.ConsolidateAsync(new[] { pdf });

        // Output should be valid, openable PDF
        using var ms = new MemoryStream(result[0]);
        var act = () => PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        act.Should().NotThrow();
    }

    // ----------------------------------------------------------------
    // Multi-document consolidation
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConsolidateAsync_FiveDocuments_MergesIntoSingleBatch()
    {
        var pdfs = Enumerable.Range(1, 5).Select(_ => CreateMinimalPdf(1)).ToList();

        var result = await _service.ConsolidateAsync(pdfs);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConsolidateAsync_FiveDocuments_CombinedHasFivePages()
    {
        var pdfs = Enumerable.Range(1, 5).Select(_ => CreateMinimalPdf(1)).ToList();

        var result = await _service.ConsolidateAsync(pdfs);

        using var ms = new MemoryStream(result[0]);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(5);
    }

    // ----------------------------------------------------------------
    // BR-4: Max 500 pages per batch
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConsolidateAsync_501Pages_SplitsIntoTwoBatches()
    {
        // 501 single-page documents = 2 batches (500 + 1)
        var pdfs = Enumerable.Range(1, 501).Select(_ => CreateMinimalPdf(1)).ToList();

        var result = await _service.ConsolidateAsync(pdfs);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConsolidateAsync_500Pages_FitsInSingleBatch()
    {
        var pdfs = Enumerable.Range(1, 500).Select(_ => CreateMinimalPdf(1)).ToList();

        var result = await _service.ConsolidateAsync(pdfs);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConsolidateAsync_1000Pages_SplitsIntoTwoBatches()
    {
        var pdfs = Enumerable.Range(1, 1000).Select(_ => CreateMinimalPdf(1)).ToList();

        var result = await _service.ConsolidateAsync(pdfs);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConsolidateAsync_1001Pages_SplitsIntoThreeBatches()
    {
        var pdfs = Enumerable.Range(1, 1001).Select(_ => CreateMinimalPdf(1)).ToList();

        var result = await _service.ConsolidateAsync(pdfs);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task ConsolidateAsync_MultiPageDocuments_SplitsCorrectly()
    {
        // 3 documents with 200 pages each = 600 pages total → 2 batches (500 + 100)
        var pdfs = Enumerable.Range(1, 3).Select(_ => CreateMinimalPdf(200)).ToList();

        var result = await _service.ConsolidateAsync(pdfs);

        result.Should().HaveCount(2);
    }

    // ----------------------------------------------------------------
    // Error resilience: invalid/null documents
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConsolidateAsync_NullInList_SkipsNullAndProcessesRest()
    {
        var pdf = CreateMinimalPdf(1);
        var pdfs = new List<byte[]?> { pdf, null!, pdf };

        var result = await _service.ConsolidateAsync(pdfs!);

        // Null is skipped; 2 valid PDFs merged
        result.Should().HaveCount(1);
        using var ms = new MemoryStream(result[0]);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task ConsolidateAsync_EmptyBytesInList_SkipsAndProcessesRest()
    {
        var pdf = CreateMinimalPdf(1);
        var pdfs = new[] { pdf, Array.Empty<byte>(), pdf };

        var result = await _service.ConsolidateAsync(pdfs);

        result.Should().HaveCount(1);
        using var ms = new MemoryStream(result[0]);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task ConsolidateAsync_CorruptPdfInList_SkipsCorruptAndProcessesRest()
    {
        var pdf = CreateMinimalPdf(1);
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03 }; // Not a PDF

        var result = await _service.ConsolidateAsync(new[] { pdf, corrupt, pdf });

        // Corrupt PDF is skipped; 2 valid merged
        result.Should().HaveCount(1);
    }

    // ----------------------------------------------------------------
    // Cancellation
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConsolidateAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _service.ConsolidateAsync(
            Enumerable.Range(1, 100).Select(_ => CreateMinimalPdf(1)),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ----------------------------------------------------------------
    // TASK-013-08: Performance test — consolidating many documents should be fast
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConsolidateAsync_100Documents_CompletesWithin10Seconds()
    {
        var pdfs = Enumerable.Range(1, 100).Select(_ => CreateMinimalPdf(1)).ToList();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var act = async () => await _service.ConsolidateAsync(pdfs, cts.Token);

        await act.Should().NotThrowAsync();
    }
}
