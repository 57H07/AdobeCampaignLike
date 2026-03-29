namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// DTO representing a diff between two template versions.
/// </summary>
public class TemplateDiffDto
{
    /// <summary>ID of the template.</summary>
    public Guid TemplateId { get; init; }

    /// <summary>The older (base) version.</summary>
    public int FromVersion { get; init; }

    /// <summary>The newer (target) version.</summary>
    public int ToVersion { get; init; }

    /// <summary>Body path of the older version.</summary>
    public string FromBodyPath { get; init; } = string.Empty;

    /// <summary>Body path of the newer version.</summary>
    public string ToBodyPath { get; init; } = string.Empty;

    /// <summary>Name in the older version.</summary>
    public string FromName { get; init; } = string.Empty;

    /// <summary>Name in the newer version.</summary>
    public string ToName { get; init; } = string.Empty;

    /// <summary>Indicates whether the name changed between versions.</summary>
    public bool NameChanged { get; init; }

    /// <summary>Indicates whether the body path changed between versions.</summary>
    public bool BodyPathChanged { get; init; }
}
