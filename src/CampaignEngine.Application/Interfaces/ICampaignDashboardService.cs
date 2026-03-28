using CampaignEngine.Application.DTOs.Campaigns;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Aggregates real-time progress metrics for active campaigns.
/// Used by GET /api/campaigns/dashboard and the Razor dashboard page.
///
/// Business rules (US-036):
///   1. Returns campaigns in Running or StepInProgress status by default.
///   2. Metrics reflect current state of TotalRecipients / ProcessedCount / SuccessCount / FailureCount.
///   3. EstimatedCompletion is calculated from the current send rate since StartedAt.
///   4. Supports optional filters: status, date range, operator.
/// </summary>
public interface ICampaignDashboardService
{
    /// <summary>
    /// Returns the aggregate dashboard with per-campaign progress metrics.
    /// </summary>
    /// <param name="filter">Optional filter parameters (status, date range, operator).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CampaignDashboardDto> GetDashboardAsync(
        DashboardFilter? filter = null,
        CancellationToken cancellationToken = default);
}
