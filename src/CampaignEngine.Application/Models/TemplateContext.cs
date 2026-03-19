namespace CampaignEngine.Application.Models;

/// <summary>
/// Structured context passed to the template renderer.
/// Encapsulates the data dictionary and optional rendering options.
/// </summary>
public sealed class TemplateContext
{
    /// <summary>
    /// Key-value data for placeholder substitution.
    /// Keys are case-insensitive (Scriban convention).
    /// Values are HTML-escaped by default to prevent XSS.
    /// </summary>
    public IDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional timeout for a single render operation.
    /// Defaults to 10 seconds (enforced by business rule BR-004).
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// When true, HTML escaping is applied to all substituted scalar values.
    /// Default is true (prevents XSS per business rule BR-001).
    /// Set to false only when rendering plain-text templates (e.g. SMS).
    /// </summary>
    public bool HtmlEncodeValues { get; init; } = true;

    /// <summary>
    /// Creates an empty context with default options.
    /// </summary>
    public static TemplateContext Empty => new();

    /// <summary>
    /// Creates a context from a plain dictionary with default options.
    /// </summary>
    public static TemplateContext FromDictionary(IDictionary<string, object?> data)
    {
        return new TemplateContext { Data = data };
    }
}
