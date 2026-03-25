using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Campaigns;

/// <summary>
/// Campaign detail page — accessible to all authenticated users.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
public class CampaignDetailModel : PageModel
{
    private readonly ICampaignService _campaignService;

    public CampaignDetailModel(ICampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    public CampaignDto? Campaign { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Campaign = await _campaignService.GetByIdAsync(id);
        if (Campaign is null)
            return NotFound();

        return Page();
    }
}
