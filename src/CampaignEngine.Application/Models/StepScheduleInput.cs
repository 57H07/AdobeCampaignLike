namespace CampaignEngine.Application.Models;

/// <summary>
/// Input descriptor for a single campaign step used by scheduling calculations.
/// </summary>
public sealed record StepScheduleInput(int StepOrder, int DelayDays);
