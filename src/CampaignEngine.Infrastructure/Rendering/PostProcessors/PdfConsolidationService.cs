using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CampaignEngine.Infrastructure.Rendering.PostProcessors;

/// <summary>
/// Merges individual per-recipient PDF documents into consolidated batch files
/// using PdfSharp (MIT-licensed, pure .NET, no native dependencies).
///
/// Business rule (US-013 BR-4): Max 500 pages per batch file.
/// Pages are imported in the order provided (recipient order preserved).
/// When a batch would exceed the limit, a new batch file is started.
///
/// Thread safety: This service is stateless. Each call creates independent PdfDocument instances.
/// Register as Scoped or Transient in DI.
/// </summary>
public sealed class PdfConsolidationService : IPdfConsolidationService
{
    /// <summary>Default maximum pages per consolidated batch. Matches BR-4.</summary>
    public const int DefaultMaxPagesPerBatch = 500;

    private readonly ILogger<PdfConsolidationService> _logger;

    /// <inheritdoc/>
    public int MaxPagesPerBatch => DefaultMaxPagesPerBatch;

    public PdfConsolidationService(ILogger<PdfConsolidationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<byte[]>> ConsolidateAsync(
        IEnumerable<byte[]> pdfDocuments,
        CancellationToken cancellationToken = default)
    {
        var documents = pdfDocuments?.ToList()
            ?? throw new ArgumentNullException(nameof(pdfDocuments));

        if (documents.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<byte[]>>(Array.Empty<byte[]>());
        }

        _logger.LogInformation(
            "Starting PDF consolidation. DocumentCount={Count} MaxPagesPerBatch={MaxPages}",
            documents.Count,
            MaxPagesPerBatch);

        var batches = new List<byte[]>();
        var currentBatch = new PdfDocument();
        var currentPageCount = 0;
        var batchNumber = 1;

        try
        {
            foreach (var pdfBytes in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (pdfBytes is null || pdfBytes.Length == 0)
                {
                    _logger.LogWarning("Skipping null or empty PDF document during consolidation.");
                    continue;
                }

                PdfDocument sourceDoc;
                try
                {
                    using var stream = new MemoryStream(pdfBytes);
                    sourceDoc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to open PDF document for consolidation — skipping document.");
                    continue;
                }

                using (sourceDoc)
                {
                    var sourcePageCount = sourceDoc.PageCount;

                    for (var pageIndex = 0; pageIndex < sourcePageCount; pageIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // When the current batch would exceed the max page count, flush it
                        if (currentPageCount >= MaxPagesPerBatch)
                        {
                            _logger.LogDebug(
                                "Flushing batch {BatchNumber} with {PageCount} pages.",
                                batchNumber,
                                currentPageCount);

                            batches.Add(SaveDocument(currentBatch));
                            currentBatch.Dispose();
                            currentBatch = new PdfDocument();
                            currentPageCount = 0;
                            batchNumber++;
                        }

                        currentBatch.AddPage(sourceDoc.Pages[pageIndex]);
                        currentPageCount++;
                    }
                }
            }

            // Flush the final (possibly partial) batch
            if (currentPageCount > 0)
            {
                _logger.LogDebug(
                    "Flushing final batch {BatchNumber} with {PageCount} pages.",
                    batchNumber,
                    currentPageCount);

                batches.Add(SaveDocument(currentBatch));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("PDF consolidation was cancelled.");
            throw;
        }
        catch (Exception ex) when (ex is not PostProcessingException)
        {
            _logger.LogError(ex, "PDF consolidation failed.");
            throw new PostProcessingException(
                $"PDF consolidation failed: {ex.Message}",
                ex,
                channel: "Letter",
                isTransient: false);
        }
        finally
        {
            currentBatch.Dispose();
        }

        _logger.LogInformation(
            "PDF consolidation completed. BatchCount={BatchCount} InputDocuments={InputCount}",
            batches.Count,
            documents.Count);

        return Task.FromResult<IReadOnlyList<byte[]>>(batches);
    }

    private static byte[] SaveDocument(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }
}
