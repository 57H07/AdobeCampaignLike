namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Request body for POST /api/templates/{id}/preview (Letter / DOCX channel).
///
/// F-401: the caller supplies all sample data that should be merged into the
/// template placeholders.  The structure must match the template manifest:
///   - <see cref="Scalars"/> covers {{ key }} placeholders.
///   - <see cref="Collections"/> covers {{ collectionKey }} / {{ item.field }} table blocks.
///   - <see cref="Conditions"/> covers {{ if key }} conditional blocks.
///
/// Business rule BR-1: preview does not persist the rendered output.
/// </summary>
public class DocxPreviewRequest
{
    /// <summary>
    /// Scalar values to merge into {{ key }} placeholders.
    /// Keys are case-sensitive.  Missing keys render as empty string.
    /// </summary>
    public Dictionary<string, string> Scalars { get; set; } = new();

    /// <summary>
    /// Collection data for {{ collectionKey }} / {{ item.field }} table row expansion.
    /// Each entry maps a collection key to its list of item dictionaries.
    /// </summary>
    public Dictionary<string, List<Dictionary<string, string>>> Collections { get; set; } = new();

    /// <summary>
    /// Boolean flags for {{ if key }} conditional block evaluation.
    /// Missing keys are treated as <c>false</c> (block is removed).
    /// </summary>
    public Dictionary<string, bool> Conditions { get; set; } = new();
}
