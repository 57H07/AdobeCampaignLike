using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.DataSources;

/// <summary>
/// Admin-only page showing data source details, field schema, and connection test actions.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class DetailsModel : PageModel
{
    private readonly IDataSourceService _dataSourceService;

    public DetailsModel(IDataSourceService dataSourceService)
    {
        _dataSourceService = dataSourceService;
    }

    public DataSourceDto? DataSource { get; private set; }
    public ConnectionTestResult? TestResult { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }
    [TempData]
    public bool IsSuccess { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        DataSource = await _dataSourceService.GetByIdAsync(id);
        if (DataSource is null)
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync(Guid id)
    {
        DataSource = await _dataSourceService.GetByIdAsync(id);
        if (DataSource is null)
            return NotFound();

        TestResult = await _dataSourceService.TestConnectionAsync(id);
        IsSuccess = TestResult.Success;
        StatusMessage = TestResult.Message;

        return Page();
    }
}
