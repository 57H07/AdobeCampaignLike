namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// DTO representing one entry in the version history of a template.
/// </summary>
public class TemplateHistoryDto
{
    /// <summary>Unique identifier of the history entry.</summary>
    public Guid Id { get; init; }

    /// <summary>ID of the parent template.</summary>
    public Guid TemplateId { get; init; }

    /// <summary>Version number at the time of this snapshot.</summary>
    public int Version { get; init; }

    /// <summary>Template name at the time of this snapshot.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Channel type at the time of this snapshot.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>Relative path to the template body file at the time of this snapshot.</summary>
    public string BodyPath { get; init; } = string.Empty;

    /// <summary>SHA-256 hex checksum at the time of this snapshot, nullable.</summary>
    public string? BodyChecksum { get; init; }

    /// <summary>Username of the user who made the change, if known.</summary>
    public string? ChangedBy { get; init; }

    /// <summary>UTC timestamp when this version was created.</summary>
    public DateTime CreatedAt { get; init; }
}
