using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.DataSources;

/// <summary>
/// Page for building filter expressions and previewing data source rows.
/// Accessible by Operator and Admin roles (Operators need to build campaign filters).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
public class FilterBuilderModel : PageModel
{
    private readonly IDataSourceService _dataSourceService;

    public FilterBuilderModel(IDataSourceService dataSourceService)
    {
        _dataSourceService = dataSourceService;
    }

    /// <summary>The data source being filtered.</summary>
    public DataSourceDto? DataSource { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        DataSource = await _dataSourceService.GetByIdAsync(id);
        if (DataSource is null)
            return NotFound();

        return Page();
    }
}
