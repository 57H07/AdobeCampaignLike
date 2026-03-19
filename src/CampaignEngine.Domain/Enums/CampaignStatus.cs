namespace CampaignEngine.Domain.Enums;

/// <summary>
/// Campaign lifecycle states.
/// </summary>
public enum CampaignStatus
{
    Draft = 1,
    Scheduled = 2,
    Running = 3,
    StepInProgress = 4,
    WaitingNext = 5,
    Completed = 6,
    PartialFailure = 7,
    ManualReview = 8
}
