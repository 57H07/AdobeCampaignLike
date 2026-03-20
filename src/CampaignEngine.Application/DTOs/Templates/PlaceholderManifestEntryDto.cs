namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// DTO representing a single placeholder manifest entry returned from the API.
/// </summary>
public class PlaceholderManifestEntryDto
{
    /// <summary>Unique identifier of this manifest entry.</summary>
    public Guid Id { get; init; }

    /// <summary>Template this entry belongs to.</summary>
    public Guid TemplateId { get; init; }

    /// <summary>Placeholder key as it appears in the template (without braces).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Type of placeholder: Scalar, Table, List, or FreeField.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// True if the value comes from a data source field.
    /// False if the value must be provided by the operator at campaign creation (freeField).
    /// </summary>
    public bool IsFromDataSource { get; init; }

    /// <summary>Optional human-readable description of the placeholder's purpose.</summary>
    public string? Description { get; init; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC last-update timestamp.</summary>
    public DateTime UpdatedAt { get; init; }
}
