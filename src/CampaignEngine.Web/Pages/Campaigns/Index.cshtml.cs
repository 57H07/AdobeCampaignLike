using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Campaigns;

/// <summary>
/// Campaign list page with active campaign status cards dashboard (US-027).
/// Accessible to all authenticated users.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
public class CampaignIndexModel : PageModel
{
    private readonly ICampaignService _campaignService;

    public CampaignIndexModel(ICampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    public IReadOnlyList<CampaignDto> Items { get; set; } = Array.Empty<CampaignDto>();
    public int Total { get; set; }
    public new int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;

    public CampaignFilterViewModel Filter { get; set; } = new();

    public string? StatusMessage { get; set; }
    public bool IsSuccess { get; set; }

    /// <summary>Active campaigns (Running or StepInProgress) for the dashboard status cards.</summary>
    public IReadOnlyList<CampaignDto> ActiveCampaigns { get; set; } = Array.Empty<CampaignDto>();

    /// <summary>Summary counts by status for the dashboard overview tiles.</summary>
    public CampaignStatusSummary StatusSummary { get; set; } = new();

    public async Task OnGetAsync(
        string? status = null,
        string? nameContains = null,
        int page = 1,
        string? message = null,
        bool? success = null)
    {
        Filter = new CampaignFilterViewModel
        {
            Status = status,
            NameContains = nameContains
        };

        if (!string.IsNullOrEmpty(message))
        {
            StatusMessage = message;
            IsSuccess = success ?? true;
        }

        CampaignStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && int.TryParse(status, out var statusInt))
        {
            statusFilter = (CampaignStatus)statusInt;
        }

        var filter = new CampaignFilter
        {
            Status = statusFilter,
            NameContains = nameContains,
            Page = Math.Max(1, page),
            PageSize = 20
        };

        var result = await _campaignService.GetPagedAsync(filter);

        Items = result.Items;
        Total = result.Total;
        Page = result.Page;
        PageSize = result.PageSize;

        // Load active campaigns for dashboard status cards (US-027)
        // Only shown when no status filter is active
        if (statusFilter is null && string.IsNullOrEmpty(nameContains))
        {
            var runningResult = await _campaignService.GetPagedAsync(new CampaignFilter
            {
                Status = CampaignStatus.Running,
                Page = 1,
                PageSize = 10
            });
            var stepResult = await _campaignService.GetPagedAsync(new CampaignFilter
            {
                Status = CampaignStatus.StepInProgress,
                Page = 1,
                PageSize = 10
            });

            ActiveCampaigns = runningResult.Items
                .Concat(stepResult.Items)
                .OrderByDescending(c => c.StartedAt ?? c.CreatedAt)
                .ToList();

            // Load status summary counts for overview tiles
            var allResult = await _campaignService.GetPagedAsync(new CampaignFilter
            {
                Page = 1,
                PageSize = 1000
            });

            StatusSummary = new CampaignStatusSummary
            {
                Draft = allResult.Items.Count(c => c.Status == "Draft"),
                Scheduled = allResult.Items.Count(c => c.Status == "Scheduled"),
                Active = allResult.Items.Count(c => c.Status is "Running" or "StepInProgress" or "WaitingNext"),
                Completed = allResult.Items.Count(c => c.Status == "Completed"),
                PartialFailure = allResult.Items.Count(c => c.Status == "PartialFailure"),
                ManualReview = allResult.Items.Count(c => c.Status == "ManualReview")
            };
        }
    }
}

/// <summary>View model for campaign list filter bar.</summary>
public class CampaignFilterViewModel
{
    public string? Status { get; set; }
    public string? NameContains { get; set; }
}

/// <summary>Summary counts of campaigns by status group for the dashboard tiles.</summary>
public class CampaignStatusSummary
{
    public int Draft { get; set; }
    public int Scheduled { get; set; }
    public int Active { get; set; }
    public int Completed { get; set; }
    public int PartialFailure { get; set; }
    public int ManualReview { get; set; }
    public int Total => Draft + Scheduled + Active + Completed + PartialFailure + ManualReview;
}
