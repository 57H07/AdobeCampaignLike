using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// IChannelDispatcher implementation for the Letter channel.
///
/// TASK-021-01: Implement LetterDispatcher.
/// TASK-021-02: PDF generation wrapper (using US-013 LetterPostProcessor).
/// TASK-021-03: PDF consolidation service with PdfSharp.
/// TASK-021-04: Print provider file drop handler (UNC or local path).
/// TASK-021-05: PDF metadata generation (manifest file).
///
/// Dispatch flow for a single letter:
///   1. Validate channel is enabled and content is non-empty.
///   2. Generate PDF from HTML using the Letter post-processor (DinkToPdf / wkhtmltopdf).
///   3. Add the per-recipient PDF and its metadata to an in-memory accumulator.
///   4. Return a per-recipient success result.
///
/// Batch consolidation (FlushBatchAsync):
///   Once all individual PDFs have been accumulated, callers MUST call FlushBatchAsync to:
///     - Consolidate individual PDFs into batches (max 500 pages each, BR-4).
///     - Write each batch PDF and its CSV manifest to the output directory.
///     - Return the list of written file paths.
///
/// NOTE: This dispatcher is stateful within a single campaign dispatch batch.
///       It must be resolved fresh per batch (Scoped lifetime in DI).
///
/// Business rules:
///   BR-1: A4 format, portrait orientation — enforced by LetterPostProcessor.
///   BR-2: Consolidation: ordered by insertion order (recipient ID / campaign sequence order).
///   BR-3: Manifest file: CSV with recipient metadata.
///   BR-4: File naming convention: CAMPAIGN_{id}_{timestamp}.pdf (handled by PrintProviderFileDropHandler).
///   BR-5: Max 500 pages per batch (handled by PdfConsolidationService).
/// </summary>
public class LetterDispatcher : IChannelDispatcher
{
    private readonly IChannelPostProcessorRegistry _postProcessorRegistry;
    private readonly IPdfConsolidationService _consolidationService;
    private readonly PrintProviderFileDropHandler _fileDropHandler;
    private readonly LetterOptions _options;
    private readonly ILogger<LetterDispatcher> _logger;

    // Accumulator: individual per-recipient PDFs collected during a batch dispatch.
    // Flushed to disk (consolidated + manifest) via FlushBatchAsync.
    private readonly List<byte[]> _accumulatedPdfs = [];
    private readonly List<LetterManifestEntry> _accumulatedEntries = [];
    private int _sequenceCounter;

    public ChannelType Channel => ChannelType.Letter;

    public LetterDispatcher(
        IChannelPostProcessorRegistry postProcessorRegistry,
        IPdfConsolidationService consolidationService,
        PrintProviderFileDropHandler fileDropHandler,
        IOptions<LetterOptions> options,
        ILogger<LetterDispatcher> logger)
    {
        _postProcessorRegistry = postProcessorRegistry;
        _consolidationService = consolidationService;
        _fileDropHandler = fileDropHandler;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates a PDF from the rendered HTML content and adds it to the batch accumulator.
    ///
    /// Per business rule BR-4 (invalid content does not fail campaign):
    /// PDF generation failures return a permanent failure result without throwing.
    /// Transient failures (PDF engine timeout) return an IsTransientFailure result.
    /// </summary>
    public async Task<DispatchResult> SendAsync(
        DispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        // ----------------------------------------------------------------
        // 1. Channel enabled check
        // ----------------------------------------------------------------
        if (!_options.IsEnabled)
        {
            _logger.LogWarning(
                "Letter channel is disabled. Skipping send for recipient '{RecipientId}'.",
                request.Recipient.ExternalRef ?? request.Recipient.DisplayName);
            return DispatchResult.Fail("Letter channel is disabled in configuration.", isTransient: false);
        }

        // ----------------------------------------------------------------
        // 2. Content validation
        // ----------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            _logger.LogWarning(
                "Letter send skipped: empty HTML content. CampaignId={CampaignId}",
                request.CampaignId);
            return DispatchResult.Fail(
                "Letter channel requires non-empty HTML content.",
                isTransient: false);
        }

        // ----------------------------------------------------------------
        // 3. Generate PDF from HTML (TASK-021-02)
        //    Uses the LetterPostProcessor registered for ChannelType.Letter.
        // ----------------------------------------------------------------
        byte[] pdfBytes;
        int pageCount;

        try
        {
            var postProcessor = _postProcessorRegistry.GetProcessor(ChannelType.Letter);

            var context = new PostProcessingContext
            {
                CampaignId = request.CampaignId,
                RecipientId = request.Recipient.ExternalRef ?? request.Recipient.DisplayName
            };

            var processingResult = await postProcessor.ProcessAsync(
                request.Content!,
                context,
                cancellationToken);

            if (processingResult.BinaryContent is null || processingResult.BinaryContent.Length == 0)
            {
                return DispatchResult.Fail(
                    "PDF generation returned an empty result.",
                    isTransient: true);
            }

            pdfBytes = processingResult.BinaryContent;
            pageCount = EstimatePageCount(pdfBytes);

            _logger.LogDebug(
                "PDF generated for recipient '{RecipientId}'. SizeBytes={Size} EstimatedPages={Pages} CampaignId={CampaignId}",
                request.Recipient.ExternalRef ?? request.Recipient.DisplayName,
                pdfBytes.Length,
                pageCount,
                request.CampaignId);
        }
        catch (PostProcessingException ex)
        {
            _logger.LogError(
                ex,
                "PDF generation failed for recipient '{RecipientId}'. CampaignId={CampaignId} IsTransient={IsTransient}",
                request.Recipient.ExternalRef ?? request.Recipient.DisplayName,
                request.CampaignId,
                ex.IsTransient);

            return DispatchResult.Fail(
                $"PDF generation failed: {ex.Message}",
                isTransient: ex.IsTransient);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "PDF generation cancelled for recipient '{RecipientId}'. CampaignId={CampaignId}",
                request.Recipient.ExternalRef,
                request.CampaignId);
            return DispatchResult.Fail("PDF generation was cancelled.", isTransient: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error generating PDF for recipient '{RecipientId}'. CampaignId={CampaignId}",
                request.Recipient.ExternalRef,
                request.CampaignId);
            return DispatchResult.Fail($"Unexpected PDF generation error: {ex.Message}", isTransient: false);
        }

        // ----------------------------------------------------------------
        // 4. Accumulate (TASK-021-03: consolidation is deferred to FlushBatchAsync)
        // ----------------------------------------------------------------
        _sequenceCounter++;
        _accumulatedPdfs.Add(pdfBytes);
        _accumulatedEntries.Add(new LetterManifestEntry(
            RecipientId: request.Recipient.ExternalRef ?? string.Empty,
            DisplayName: request.Recipient.DisplayName,
            SequenceInBatch: _sequenceCounter,
            PageCount: pageCount,
            BatchFileName: string.Empty   // Updated by FlushBatchAsync
        ));

        // Individual message ID: sequence number within this dispatcher instance.
        var messageId = $"LETTER-{request.CampaignId:N}-{_sequenceCounter:D6}";

        _logger.LogInformation(
            "PDF for recipient '{RecipientId}' accumulated (sequence {Sequence}). CampaignId={CampaignId}",
            request.Recipient.ExternalRef ?? request.Recipient.DisplayName,
            _sequenceCounter,
            request.CampaignId);

        return DispatchResult.Ok(messageId: messageId);
    }

    /// <summary>
    /// Consolidates all accumulated per-recipient PDFs into batch files and
    /// writes them (plus optional manifests) to the output directory.
    ///
    /// TASK-021-03: PDF consolidation.
    /// TASK-021-04: File drop to output directory.
    /// TASK-021-05: CSV manifest.
    ///
    /// Must be called once after all individual SendAsync calls are complete.
    /// Returns a list of written batch PDF file paths.
    ///
    /// Throws <see cref="LetterDispatchException"/> if the output directory is not configured
    /// or if a file I/O error occurs (transient).
    /// </summary>
    /// <param name="campaignId">Campaign identifier used in the output file names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of written PDF file paths (one per batch).</returns>
    public async Task<IReadOnlyList<string>> FlushBatchAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        if (_accumulatedPdfs.Count == 0)
        {
            _logger.LogInformation(
                "No accumulated PDFs to flush for CampaignId={CampaignId}.",
                campaignId);
            return [];
        }

        _logger.LogInformation(
            "Flushing {Count} accumulated PDFs for CampaignId={CampaignId}.",
            _accumulatedPdfs.Count,
            campaignId);

        // ----------------------------------------------------------------
        // Consolidate individual PDFs into batches (TASK-021-03)
        // ----------------------------------------------------------------
        var batches = await _consolidationService.ConsolidateAsync(_accumulatedPdfs, cancellationToken);

        _logger.LogInformation(
            "Consolidated {DocumentCount} PDFs into {BatchCount} batch(es). CampaignId={CampaignId}",
            _accumulatedPdfs.Count,
            batches.Count,
            campaignId);

        var timestamp = DateTime.UtcNow;
        var writtenPaths = new List<string>(batches.Count);

        // Track which entries belong to which batch for the manifest.
        // We assign entries to batches proportionally by page accumulation.
        var batchManifestEntries = BuildBatchManifestEntries(batches.Count);

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchNumber = batchIndex + 1;
            var batchPdf = batches[batchIndex];

            // Build the CSV manifest for this batch (TASK-021-05)
            var manifestEntries = batchManifestEntries[batchIndex];
            var batchFileName = BuildBatchFileName(campaignId, timestamp, batchNumber);

            // Update manifest entries with the real batch file name
            var enrichedEntries = manifestEntries
                .Select(e => e with { BatchFileName = batchFileName })
                .ToList();

            var manifestCsv = _options.GenerateManifest
                ? LetterManifestGenerator.BuildCsv(enrichedEntries)
                : null;

            // Write PDF + manifest to output directory (TASK-021-04)
            var pdfPath = await _fileDropHandler.WriteAsync(
                batchPdf,
                manifestCsv,
                campaignId,
                batchNumber,
                timestamp,
                cancellationToken);

            writtenPaths.Add(pdfPath);

            _logger.LogInformation(
                "Batch {BatchNumber}/{BatchTotal} written to '{Path}'. CampaignId={CampaignId}",
                batchNumber,
                batches.Count,
                pdfPath,
                campaignId);
        }

        // Clear the accumulator after successful flush.
        _accumulatedPdfs.Clear();
        _accumulatedEntries.Clear();
        _sequenceCounter = 0;

        return writtenPaths;
    }

    /// <summary>Number of PDFs accumulated but not yet flushed.</summary>
    public int AccumulatedCount => _accumulatedPdfs.Count;

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Distributes manifest entries across batches.
    ///
    /// The consolidation service splits by page count, so we need to assign each entry
    /// to the correct batch. We use a simple sequential assignment based on cumulative
    /// page counts matching the max-pages-per-batch limit.
    /// </summary>
    private List<List<LetterManifestEntry>> BuildBatchManifestEntries(int batchCount)
    {
        var result = Enumerable.Range(0, batchCount)
            .Select(_ => new List<LetterManifestEntry>())
            .ToList();

        if (batchCount == 1)
        {
            result[0].AddRange(_accumulatedEntries);
            return result;
        }

        var maxPages = _consolidationService.MaxPagesPerBatch;
        var cumulativePages = 0;
        var batchIndex = 0;

        foreach (var entry in _accumulatedEntries)
        {
            cumulativePages += entry.PageCount;
            if (cumulativePages > maxPages && batchIndex < batchCount - 1)
            {
                batchIndex++;
                cumulativePages = entry.PageCount;
            }

            result[batchIndex].Add(entry);
        }

        return result;
    }

    /// <summary>
    /// Builds the base file name (without extension) for a batch.
    /// Convention: CAMPAIGN_{campaignId}_{timestamp}_{batchNumber:D3}
    /// </summary>
    private string BuildBatchFileName(Guid campaignId, DateTime timestamp, int batchNumber)
    {
        var prefix = _options.FileNamePrefix ?? "CAMPAIGN";
        var ts = timestamp.ToString("yyyyMMddHHmmss");
        return $"{prefix}_{campaignId:N}_{ts}_{batchNumber:D3}";
    }

    /// <summary>
    /// Estimates the page count of a PDF from its byte content using a lightweight
    /// heuristic (counting /Type /Page occurrences). This is used for manifest metadata
    /// and batch distribution calculations — it does not need to be perfectly accurate.
    ///
    /// For a precise count, use PdfSharp.PdfReader (heavier, not needed here).
    /// </summary>
    private static int EstimatePageCount(byte[] pdfBytes)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
            return 1;

        // Count "/Type /Page" entries which appear once per page in PDF structure.
        // This is a fast heuristic; exact parsing would require a PDF library.
        try
        {
            var content = System.Text.Encoding.ASCII.GetString(pdfBytes);
            var marker = "/Type /Page";
            var count = 0;
            var startIndex = 0;
            while ((startIndex = content.IndexOf(marker, startIndex, StringComparison.Ordinal)) >= 0)
            {
                count++;
                startIndex += marker.Length;
            }

            return count > 0 ? count : 1;
        }
        catch
        {
            return 1;
        }
    }
}
