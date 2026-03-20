namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Result of extracting placeholder keys from a template's HTML body.
/// Used to auto-detect which placeholders need to be declared in the manifest.
/// </summary>
public class PlaceholderExtractionResult
{
    /// <summary>
    /// All scalar placeholder keys found (e.g., {{ key }} syntax, not inside blocks).
    /// </summary>
    public IReadOnlyList<string> ScalarKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Table/iteration block keys found (e.g., {{ for row in tableName }} syntax).
    /// </summary>
    public IReadOnlyList<string> IterationKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// All unique placeholder keys found across all types (scalar + iteration).
    /// </summary>
    public IReadOnlyList<string> AllKeys { get; init; } = Array.Empty<string>();
}
