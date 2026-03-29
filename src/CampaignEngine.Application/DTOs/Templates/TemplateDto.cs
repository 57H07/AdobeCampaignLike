namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// DTO representing a template returned from the API.
/// </summary>
public class TemplateDto
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Display name (unique within channel).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Channel type string: Email, Letter, or Sms.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>Relative path from storage root to the template body file.</summary>
    public string BodyPath { get; init; } = string.Empty;

    /// <summary>SHA-256 hex checksum of the template body file (64 chars), nullable.</summary>
    public string? BodyChecksum { get; init; }

    /// <summary>Template lifecycle status: Draft, Published, Archived.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Auto-incrementing version number.</summary>
    public int Version { get; init; }

    /// <summary>Whether this template is used as a sub-template block.</summary>
    public bool IsSubTemplate { get; init; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC last-update timestamp.</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Declared placeholder manifest entries.
    /// Populated when the template is loaded with its manifest (e.g., from detail view).
    /// May be empty if placeholders are not loaded in the current context.
    /// </summary>
    public IReadOnlyList<PlaceholderManifestEntryDto> PlaceholderManifests { get; init; }
        = Array.Empty<PlaceholderManifestEntryDto>();
}
