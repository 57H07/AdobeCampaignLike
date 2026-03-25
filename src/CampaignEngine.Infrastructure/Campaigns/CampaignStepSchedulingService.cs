using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Calculates scheduled execution dates for multi-step campaign sequences.
/// Algorithm:
///   - Step 1: campaignStart + Step1.DelayDays
///   - Step N (N > 1): Step(N-1).ScheduledAt + StepN.DelayDays
/// This means DelayDays is always relative to the previous step's execution
/// (or campaign start for step 1). DelayDays = 0 means same day as the base date.
/// </summary>
public sealed class CampaignStepSchedulingService : ICampaignStepSchedulingService
{
    /// <inheritdoc />
    public IReadOnlyList<StepScheduleEntry> CalculateStepDates(
        DateTime campaignStart,
        IReadOnlyList<StepScheduleInput> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count == 0)
            return Array.Empty<StepScheduleEntry>();

        // Steps must be in StepOrder order for sequential delay calculation
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();

        var results = new List<StepScheduleEntry>(ordered.Count);
        DateTime previousDate = campaignStart;

        foreach (var step in ordered)
        {
            // Each step is scheduled DelayDays after the previous step (or campaign start)
            var scheduledAt = previousDate.AddDays(step.DelayDays);
            results.Add(new StepScheduleEntry(step.StepOrder, scheduledAt));
            previousDate = scheduledAt;
        }

        return results.AsReadOnly();
    }
}
