namespace CampaignEngine.Application.Models;

/// <summary>
/// The computed scheduled execution date for a single campaign step.
/// </summary>
public sealed record StepScheduleEntry(int StepOrder, DateTime ScheduledAt);
