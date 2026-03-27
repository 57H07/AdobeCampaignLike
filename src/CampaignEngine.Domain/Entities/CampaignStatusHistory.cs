using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Records a single status transition event for a campaign.
/// Provides an immutable audit trail of all lifecycle changes.
/// </summary>
public class CampaignStatusHistory : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public CampaignStatus FromStatus { get; set; }

    public CampaignStatus ToStatus { get; set; }

    /// <summary>Optional free-text reason for the transition.</summary>
    public string? Reason { get; set; }

    /// <summary>UTC timestamp when the transition occurred.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Campaign? Campaign { get; set; }
}
