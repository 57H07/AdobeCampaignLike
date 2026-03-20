namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Result of validating the completeness of a template's placeholder manifest.
/// Indicates whether all placeholders used in the HTML body are declared.
/// </summary>
public class ManifestValidationResult
{
    /// <summary>
    /// True if every placeholder key found in the template HTML has a corresponding manifest entry.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Placeholder keys that appear in the template HTML but are not declared in the manifest.
    /// These must be declared before the template can be published.
    /// </summary>
    public IReadOnlyList<string> UndeclaredKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Placeholder keys declared in the manifest but not found in the current template HTML.
    /// These may be orphan declarations (informational only, not an error).
    /// </summary>
    public IReadOnlyList<string> OrphanKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Human-readable summary of the validation result.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}
