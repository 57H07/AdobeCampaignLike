using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// REST API endpoint for the campaign progress dashboard (US-036).
///
/// Endpoint:
///   GET /api/campaigns/dashboard
///
/// Returns aggregate real-time metrics for active campaigns (Running or StepInProgress by default).
/// Supports optional filters: status, date range, operator.
///
/// Authentication: X-Api-Key header or authenticated session.
/// </summary>
[ApiController]
[Route("api/campaigns/dashboard")]
[Produces("application/json")]
public class CampaignDashboardController : ControllerBase
{
    private readonly ICampaignDashboardService _dashboardService;

    public CampaignDashboardController(ICampaignDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Returns aggregate progress metrics for active campaigns.
    ///
    /// By default returns campaigns in Running or StepInProgress status.
    /// All query parameters are optional.
    /// </summary>
    /// <param name="status">
    /// Comma-separated status filter (e.g. "Running,StepInProgress").
    /// Accepts status names or integer values.
    /// Defaults to Running and StepInProgress.
    /// </param>
    /// <param name="startedFrom">
    /// UTC lower bound for campaign StartedAt (ISO-8601, e.g. 2026-01-01T00:00:00Z).
    /// </param>
    /// <param name="startedTo">
    /// UTC upper bound for campaign StartedAt (ISO-8601).
    /// </param>
    /// <param name="createdBy">Filter by operator username.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(CampaignDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CampaignDashboardDto>> GetDashboard(
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startedFrom = null,
        [FromQuery] DateTime? startedTo = null,
        [FromQuery] string? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new DashboardFilter
        {
            Status = status,
            StartedFrom = startedFrom,
            StartedTo = startedTo,
            CreatedBy = createdBy
        };

        var dashboard = await _dashboardService.GetDashboardAsync(filter, cancellationToken);
        return Ok(dashboard);
    }
}
