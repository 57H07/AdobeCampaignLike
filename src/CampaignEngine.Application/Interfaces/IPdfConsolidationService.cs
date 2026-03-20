namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Merges multiple individual PDF documents (one per recipient letter) into batch PDF files.
/// Each batch file contains at most <see cref="MaxPagesPerBatch"/> pages (business rule BR-4).
///
/// Business rule (US-013 BR-4): PDF consolidation max 500 pages per batch file.
/// </summary>
public interface IPdfConsolidationService
{
    /// <summary>
    /// Maximum number of pages per consolidated batch file.
    /// </summary>
    int MaxPagesPerBatch { get; }

    /// <summary>
    /// Merges the provided PDF byte arrays into one or more batch PDFs.
    /// Returns a list of consolidated PDF byte arrays, each respecting the page limit.
    /// Page order is preserved: recipients are included in the order provided.
    /// </summary>
    /// <param name="pdfDocuments">Ordered collection of individual PDF byte arrays.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One or more consolidated PDF byte arrays.
    /// Each entry represents one batch file (max <see cref="MaxPagesPerBatch"/> pages).
    /// </returns>
    Task<IReadOnlyList<byte[]>> ConsolidateAsync(
        IEnumerable<byte[]> pdfDocuments,
        CancellationToken cancellationToken = default);
}
