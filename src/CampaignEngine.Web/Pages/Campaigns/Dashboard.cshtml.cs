using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Campaigns;

/// <summary>
/// Campaign progress dashboard page (US-036).
/// Shows all active campaigns (Running / StepInProgress) with per-campaign
/// metrics: total, processed, sent, failed counts, progress percentage,
/// estimated completion time, and multi-step timeline visualization.
///
/// Auto-refreshes every 10 seconds via JavaScript polling of
/// GET /api/campaigns/dashboard.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
public class CampaignDashboardModel : PageModel
{
    private readonly ICampaignDashboardService _dashboardService;

    public CampaignDashboardModel(ICampaignDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>Initial dashboard data loaded on page load (server-side render).</summary>
    public CampaignDashboardDto Dashboard { get; set; } = new();

    /// <summary>Filter: status (comma-separated). Defaults to Running,StepInProgress.</summary>
    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    /// <summary>Filter: campaigns started on or after this UTC date.</summary>
    [BindProperty(SupportsGet = true)]
    public DateTime? StartedFrom { get; set; }

    /// <summary>Filter: campaigns started on or before this UTC date.</summary>
    [BindProperty(SupportsGet = true)]
    public DateTime? StartedTo { get; set; }

    /// <summary>Filter: operator (createdBy username).</summary>
    [BindProperty(SupportsGet = true)]
    public string? CreatedBy { get; set; }

    public async Task OnGetAsync()
    {
        var filter = new DashboardFilter
        {
            Status = StatusFilter,
            StartedFrom = StartedFrom,
            StartedTo = StartedTo,
            CreatedBy = CreatedBy
        };

        Dashboard = await _dashboardService.GetDashboardAsync(filter);
    }
}
