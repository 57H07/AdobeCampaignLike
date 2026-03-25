using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Campaigns;

/// <summary>
/// Campaign list page — accessible to all authenticated users.
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
    }
}

/// <summary>View model for campaign list filter bar.</summary>
public class CampaignFilterViewModel
{
    public string? Status { get; set; }
    public string? NameContains { get; set; }
}
