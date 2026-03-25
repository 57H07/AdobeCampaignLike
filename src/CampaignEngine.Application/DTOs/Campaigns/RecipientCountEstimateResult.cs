namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Result of recipient count estimation for a campaign.
/// Returned by GET /api/campaigns/estimate-recipients.
/// </summary>
public class RecipientCountEstimateResult
{
    /// <summary>Estimated number of recipients matching the campaign criteria.</summary>
    public int EstimatedCount { get; init; }

    /// <summary>Whether the estimate was computed successfully.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Error message if the estimate could not be computed.</summary>
    public string? ErrorMessage { get; init; }
}
