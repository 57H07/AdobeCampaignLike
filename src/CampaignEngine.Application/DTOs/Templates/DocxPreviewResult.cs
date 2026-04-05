namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Result of a DOCX template preview operation (F-401).
///
/// Contains the fully-rendered DOCX bytes ready for download, together with
/// metadata the controller needs to set response headers.
/// </summary>
public class DocxPreviewResult
{
    /// <summary>
    /// ID of the template that was previewed.
    /// </summary>
    public Guid TemplateId { get; init; }

    /// <summary>
    /// Name of the template.  Used to build the Content-Disposition filename:
    /// <c>attachment; filename="preview-{TemplateName}.docx"</c>.
    /// </summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>
    /// The rendered DOCX document bytes.
    /// </summary>
    public byte[] DocxBytes { get; init; } = Array.Empty<byte>();
}
