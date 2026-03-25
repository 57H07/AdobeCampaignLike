using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Campaigns;

/// <summary>
/// Campaign detail page — accessible to all authenticated users.
/// Displays campaign summary, steps, and template snapshots (US-025).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
public class CampaignDetailModel : PageModel
{
    private readonly ICampaignService _campaignService;
    private readonly ITemplateSnapshotService _snapshotService;

    public CampaignDetailModel(
        ICampaignService campaignService,
        ITemplateSnapshotService snapshotService)
    {
        _campaignService = campaignService;
        _snapshotService = snapshotService;
    }

    public CampaignDto? Campaign { get; set; }

    /// <summary>
    /// Template snapshots frozen when the campaign was scheduled.
    /// Empty if the campaign has not yet been scheduled.
    /// </summary>
    public IReadOnlyList<TemplateSnapshotDto> Snapshots { get; set; } = Array.Empty<TemplateSnapshotDto>();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Campaign = await _campaignService.GetByIdAsync(id);
        if (Campaign is null)
            return NotFound();

        Snapshots = await _snapshotService.GetSnapshotsForCampaignAsync(id);

        return Page();
    }
}
