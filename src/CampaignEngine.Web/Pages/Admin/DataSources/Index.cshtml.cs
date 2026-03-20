using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.DataSources;

/// <summary>
/// Admin-only page listing all data sources with filter, pagination, and activate/deactivate actions.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class IndexModel : PageModel
{
    private readonly IDataSourceService _dataSourceService;

    public IndexModel(IDataSourceService dataSourceService)
    {
        _dataSourceService = dataSourceService;
    }

    public IReadOnlyList<DataSourceDto> DataSources { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public DataSourceFilterViewModel Filter { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }
    [TempData]
    public bool IsSuccess { get; set; }

    public async Task OnGetAsync(
        string? nameContains = null,
        string? type = null,
        string? isActive = null,
        int page = 1)
    {
        Filter = new DataSourceFilterViewModel
        {
            NameContains = nameContains,
            TypeValue = type,
            IsActiveValue = isActive
        };

        DataSourceType? typeEnum = null;
        if (!string.IsNullOrEmpty(type) && int.TryParse(type, out var typeInt))
            typeEnum = (DataSourceType)typeInt;

        bool? isActiveBool = null;
        if (!string.IsNullOrEmpty(isActive) && bool.TryParse(isActive, out var isActiveParsed))
            isActiveBool = isActiveParsed;

        var filter = new DataSourceFilter
        {
            Type = typeEnum,
            IsActive = isActiveBool,
            NameContains = nameContains,
            Page = Math.Max(1, page),
            PageSize = 20
        };

        var result = await _dataSourceService.GetAllAsync(filter);

        DataSources = result.Items;
        TotalCount = result.Total;
        TotalPages = result.TotalPages;
        CurrentPage = result.Page;
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid id, bool isActive)
    {
        try
        {
            var ds = await _dataSourceService.SetActiveAsync(id, isActive);
            IsSuccess = true;
            StatusMessage = $"Data source '{ds.Name}' has been {(isActive ? "activated" : "deactivated")}.";
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"Failed to update status: {ex.Message}";
        }

        return RedirectToPage();
    }
}

/// <summary>
/// View-layer filter state to preserve form values across postbacks.
/// </summary>
public class DataSourceFilterViewModel
{
    public string? NameContains { get; set; }
    public string? TypeValue { get; set; }
    public string? IsActiveValue { get; set; }
}
