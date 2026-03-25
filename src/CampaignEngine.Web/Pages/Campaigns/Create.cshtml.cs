using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Campaigns;

/// <summary>
/// Campaign creation wizard page.
/// Loads published templates and active data sources for the wizard dropdowns.
/// Actual campaign creation is handled via POST /api/campaigns (AJAX from wizard JS).
/// Operator and Admin roles only.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
public class CreateCampaignModel : PageModel
{
    private readonly ITemplateService _templateService;
    private readonly IDataSourceService _dataSourceService;

    public CreateCampaignModel(
        ITemplateService templateService,
        IDataSourceService dataSourceService)
    {
        _templateService = templateService;
        _dataSourceService = dataSourceService;
    }

    /// <summary>Published templates available for selection (non-subtemplates).</summary>
    public IReadOnlyList<TemplateDto> PublishedTemplates { get; set; } = Array.Empty<TemplateDto>();

    /// <summary>Active data sources available for selection.</summary>
    public IReadOnlyList<DataSourceDto> DataSources { get; set; } = Array.Empty<DataSourceDto>();

    public async Task OnGetAsync()
    {
        // Load all published, non-sub-templates
        var pagedResult = await _templateService.GetPagedAsync(
            channel: null,
            status: TemplateStatus.Published,
            page: 1,
            pageSize: 200);

        PublishedTemplates = pagedResult.Items
            .Where(t => !t.IsSubTemplate)
            .ToList();

        // Load active data sources
        var dsResult = await _dataSourceService.GetAllAsync(new DataSourceFilter
        {
            IsActive = true,
            Page = 1,
            PageSize = 200
        });

        DataSources = dsResult.Items;
    }
}
