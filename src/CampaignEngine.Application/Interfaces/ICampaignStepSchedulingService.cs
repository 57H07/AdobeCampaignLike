using CampaignEngine.Application.Models;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Calculates the scheduled execution date for each step in a multi-step campaign.
/// Business rules:
///   - Step 1 delay is relative to campaign start date (or now if immediate).
///   - Subsequent step delays are relative to the previous step's scheduled execution date.
///   - DelayDays = 0 means same date as the base date (immediate / no delay from previous step).
/// </summary>
public interface ICampaignStepSchedulingService
{
    /// <summary>
    /// Calculates scheduled execution dates for an ordered list of steps.
    /// </summary>
    /// <param name="campaignStart">The campaign start date (UTC). Use DateTime.UtcNow for immediate campaigns.</param>
    /// <param name="steps">Ordered step configurations (must already be sorted by StepOrder).</param>
    /// <returns>List of <see cref="StepScheduleEntry"/> with computed execution dates in step order.</returns>
    IReadOnlyList<StepScheduleEntry> CalculateStepDates(
        DateTime campaignStart,
        IReadOnlyList<StepScheduleInput> steps);
}
