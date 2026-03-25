namespace CampaignEngine.Domain.Enums;

/// <summary>
/// Lifecycle states for a campaign chunk (batch of recipients).
/// </summary>
public enum ChunkStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    PartialFailure = 5
}
