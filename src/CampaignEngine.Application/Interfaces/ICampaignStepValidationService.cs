using CampaignEngine.Application.DTOs.Campaigns;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Validates multi-step campaign step configuration before persistence.
/// Enforces business rules: step count (max 10), order uniqueness, delay constraints.
/// </summary>
public interface ICampaignStepValidationService
{
    /// <summary>
    /// Validates a list of campaign step requests.
    /// Throws <see cref="CampaignEngine.Domain.Exceptions.ValidationException"/> if any rule is violated.
    /// </summary>
    /// <param name="steps">The ordered list of step requests to validate.</param>
    void Validate(IReadOnlyList<CreateCampaignStepRequest> steps);
}
