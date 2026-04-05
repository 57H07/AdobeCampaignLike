namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Orchestrates the full DOCX rendering pipeline for a single recipient:
///   1. DocxRunMerger     — merges split Word XML runs so placeholders are recognised.
///   2. DocxPlaceholderReplacer — replaces {{ key }} scalar placeholders.
///   3. DocxTableCollectionRenderer — expands {{ collection }} / {{ end }} table rows.
///   4. DocxConditionalBlockRenderer — evaluates {{ if key }} / {{ end }} blocks.
///
/// The renderer operates on an in-memory copy of the DOCX so the original file
/// is never modified.  The result is a new byte array ready for download or dispatch.
///
/// F-401 / US-020 contract:
///   - Preview does not persist the rendered output (ephemeral).
///   - Sample data scalars, collections, and conditions are all applied.
/// </summary>
public interface IDocxTemplateRenderer
{
    /// <summary>
    /// Renders a DOCX template byte array against the provided recipient data.
    /// </summary>
    /// <param name="docxBytes">
    /// The raw bytes of the source DOCX template.  The array is not modified.
    /// </param>
    /// <param name="scalars">
    /// Scalar placeholder values: key → string value.
    /// Keys are case-sensitive.  Missing keys render as empty string.
    /// </param>
    /// <param name="collections">
    /// Collection data for table row expansion: collection key → list of item dictionaries.
    /// Pass an empty dictionary when the template has no collections.
    /// </param>
    /// <param name="conditions">
    /// Boolean flags for conditional block evaluation: key → bool.
    /// Missing keys are treated as <c>false</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A byte array containing the fully rendered DOCX document.
    /// </returns>
    Task<byte[]> RenderAsync(
        byte[] docxBytes,
        Dictionary<string, string> scalars,
        Dictionary<string, List<Dictionary<string, string>>> collections,
        Dictionary<string, bool> conditions,
        CancellationToken cancellationToken = default);
}
