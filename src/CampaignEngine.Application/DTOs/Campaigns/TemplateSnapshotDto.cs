using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// DTO representing an immutable template snapshot frozen at campaign scheduling time.
/// </summary>
public class TemplateSnapshotDto
{
    /// <summary>Unique identifier of this snapshot.</summary>
    public Guid Id { get; init; }

    /// <summary>ID of the original template from which this snapshot was created.</summary>
    public Guid OriginalTemplateId { get; init; }

    /// <summary>Template version captured at snapshot time.</summary>
    public int TemplateVersion { get; init; }

    /// <summary>Template name at snapshot time.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Channel type (Email, Sms, Letter).</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>
    /// Fully resolved HTML body including all sub-template content,
    /// frozen at campaign scheduling time.
    /// </summary>
    public string ResolvedHtmlBody { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this snapshot was created.</summary>
    public DateTime CreatedAt { get; init; }
}
