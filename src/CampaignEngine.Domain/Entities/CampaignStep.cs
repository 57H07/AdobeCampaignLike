using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents a single step in a multi-step campaign sequence.
/// Steps are ordered and can have channel-specific delays.
/// </summary>
public class CampaignStep : AuditableEntity
{
    public Guid CampaignId { get; set; }
    public int StepOrder { get; set; }
    public ChannelType Channel { get; set; }
    public Guid TemplateId { get; set; }

    /// <summary>
    /// Delay in days from previous step (0 = immediate).
    /// </summary>
    public int DelayDays { get; set; } = 0;

    /// <summary>
    /// Optional JSON-serialized step-specific filter expression.
    /// AND-combined with the campaign base filter.
    /// </summary>
    public string? StepFilter { get; set; }

    /// <summary>
    /// ID of the frozen template snapshot used when this step was scheduled.
    /// </summary>
    public Guid? TemplateSnapshotId { get; set; }

    public DateTime? ScheduledAt { get; set; }
    public DateTime? ExecutedAt { get; set; }

    // Navigation properties
    public Campaign? Campaign { get; set; }
    public TemplateSnapshot? TemplateSnapshot { get; set; }
}
