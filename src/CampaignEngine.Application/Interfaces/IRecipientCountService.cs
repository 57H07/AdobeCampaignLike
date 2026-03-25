using CampaignEngine.Application.DTOs.Campaigns;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service that estimates the number of recipients that will be targeted
/// by a campaign's data source + filter combination.
/// Used for campaign preview before execution.
/// </summary>
public interface IRecipientCountService
{
    /// <summary>
    /// Estimates the number of records in the specified data source
    /// that match the provided filter expression.
    /// </summary>
    /// <param name="request">Data source ID and optional filter expression.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An estimate result with the count or an error message.</returns>
    Task<RecipientCountEstimateResult> EstimateAsync(
        EstimateRecipientsRequest request,
        CancellationToken cancellationToken = default);
}
