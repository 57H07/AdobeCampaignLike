using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents a reusable message template with dynamic content.
/// Templates support soft delete and version history.
/// </summary>
public class Template : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string HtmlBody { get; set; } = string.Empty;
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    public int Version { get; set; } = 1;
    public bool IsSubTemplate { get; set; } = false;
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<PlaceholderManifestEntry> PlaceholderManifests { get; set; } = new List<PlaceholderManifestEntry>();
    public ICollection<TemplateHistory> History { get; set; } = new List<TemplateHistory>();
}
