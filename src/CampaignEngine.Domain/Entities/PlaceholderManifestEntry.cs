using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Declares a single typed placeholder within a template.
/// Each entry specifies the placeholder key, type, and data source.
/// </summary>
public class PlaceholderManifestEntry : AuditableEntity
{
    public Guid TemplateId { get; set; }
    public string Key { get; set; } = string.Empty;
    public PlaceholderType Type { get; set; }
    public bool IsFromDataSource { get; set; } = true;
    public string? Description { get; set; }

    // Navigation property
    public Template? Template { get; set; }
}
