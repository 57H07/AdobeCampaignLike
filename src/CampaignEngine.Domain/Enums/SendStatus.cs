namespace CampaignEngine.Domain.Enums;

/// <summary>
/// Individual send operation statuses.
/// </summary>
public enum SendStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Retrying = 4
}
