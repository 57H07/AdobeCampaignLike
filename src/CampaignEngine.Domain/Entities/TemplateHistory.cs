using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Immutable snapshot of a template version for audit and rollback purposes.
/// Version history is never deleted.
/// </summary>
public class TemplateHistory : AuditableEntity
{
    public Guid TemplateId { get; set; }
    public int Version { get; set; }
    public string HtmlBody { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string? ChangedBy { get; set; }

    // Navigation property
    public Template? Template { get; set; }
}
