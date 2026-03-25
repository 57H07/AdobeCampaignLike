using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Validates campaign step configuration against multi-step business rules.
/// Rules enforced:
///   1. Step count: 1 to 10 (inclusive).
///   2. StepOrder values must be unique within the campaign.
///   3. StepOrder values must be positive integers (1-based).
///   4. DelayDays must be >= 0 (no negative delays).
///   5. Step 1 may have DelayDays > 0 (delay from campaign start).
/// </summary>
public sealed class CampaignStepValidationService : ICampaignStepValidationService
{
    private const int MaxSteps = 10;

    /// <inheritdoc />
    public void Validate(IReadOnlyList<CreateCampaignStepRequest> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var errors = new Dictionary<string, string[]>();

        // Rule 1: step count bounds
        if (steps.Count == 0)
        {
            errors["steps"] = ["At least one campaign step is required."];
            throw new ValidationException(errors);
        }

        if (steps.Count > MaxSteps)
        {
            errors["steps"] = [$"A campaign may have at most {MaxSteps} steps. Provided: {steps.Count}."];
            throw new ValidationException(errors);
        }

        // Rule 2: StepOrder uniqueness
        var orders = steps.Select(s => s.StepOrder).ToList();
        var duplicates = orders.GroupBy(o => o).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            errors["steps"] = [$"Step orders must be unique. Duplicate order(s): {string.Join(", ", duplicates)}."];
            throw new ValidationException(errors);
        }

        // Rule 3: StepOrder must be positive
        var invalidOrders = orders.Where(o => o < 1).ToList();
        if (invalidOrders.Count > 0)
        {
            errors["steps"] = [$"Step orders must be positive integers (1-based). Invalid: {string.Join(", ", invalidOrders)}."];
            throw new ValidationException(errors);
        }

        // Rule 4: DelayDays must be non-negative
        var negativeDelays = steps.Where(s => s.DelayDays < 0).Select(s => s.StepOrder).ToList();
        if (negativeDelays.Count > 0)
        {
            errors["steps"] = [$"Step delay must be non-negative (0 = immediate). Steps with negative delay: {string.Join(", ", negativeDelays)}."];
            throw new ValidationException(errors);
        }
    }
}
