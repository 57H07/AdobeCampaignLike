namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Result of a template preview operation.
/// Contains the channel-specific rendered output and diagnostic information.
/// </summary>
public class TemplatePreviewResult
{
    /// <summary>
    /// ID of the template that was previewed.
    /// </summary>
    public Guid TemplateId { get; init; }

    /// <summary>
    /// Channel of the template (Email, Letter, Sms).
    /// </summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>
    /// MIME content type of the rendered output.
    /// "text/html" for Email, "application/pdf" for Letter, "text/plain" for SMS.
    /// </summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    /// Rendered text output (for Email HTML and SMS channels).
    /// Null when the output is binary (Letter/PDF).
    /// </summary>
    public string? TextContent { get; init; }

    /// <summary>
    /// Rendered binary output as base64 (for Letter/PDF channel).
    /// Null when the output is text.
    /// </summary>
    public string? Base64Content { get; init; }

    /// <summary>
    /// Sample data rows fetched from the data source (up to SampleRowCount).
    /// Each row is a dictionary of field name to value.
    /// </summary>
    public IReadOnlyList<IDictionary<string, object?>> SampleRows { get; init; } = [];

    /// <summary>
    /// Index of the sample row used for this render (0-based).
    /// </summary>
    public int RowUsed { get; init; }

    /// <summary>
    /// Total number of sample rows fetched.
    /// </summary>
    public int TotalSampleRows { get; init; }

    /// <summary>
    /// Placeholder keys present in the template that had no matching value
    /// in the sample data row. These are highlighted for the designer.
    /// </summary>
    public IReadOnlyList<string> MissingPlaceholders { get; init; } = [];

    /// <summary>
    /// Whether the preview rendered successfully.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Error message when IsSuccess is false.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
