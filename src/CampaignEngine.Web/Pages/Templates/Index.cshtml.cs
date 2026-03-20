using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Template list page — accessible to all authenticated users.
/// DELETE action (soft delete) restricted to Designer and Admin roles.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
public class TemplatesIndexModel : PageModel
{
    private readonly ITemplateService _templateService;

    public TemplatesIndexModel(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    // ----------------------------------------------------------------
    // Bound filter properties
    // ----------------------------------------------------------------

    [BindProperty(SupportsGet = true)]
    public TemplateFilterViewModel Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public new int Page { get; set; } = 1;

    // ----------------------------------------------------------------
    // Result properties
    // ----------------------------------------------------------------

    public IReadOnlyList<TemplateDto> Items { get; private set; } = Array.Empty<TemplateDto>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public int PageSize { get; private set; } = 20;

    public string? StatusMessage { get; set; }
    public bool IsSuccess { get; set; }

    public async Task OnGetAsync(string? message = null, bool success = false)
    {
        StatusMessage = message;
        IsSuccess = success;

        await LoadTemplatesAsync();
    }

    /// <summary>
    /// Soft-deletes a template.
    /// Access is role-checked in the handler: only Designer and Admin can proceed.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        // Enforce Designer/Admin access at handler level (Authorize on handlers is not supported for Razor Pages)
        if (!User.IsInRole("Designer") && !User.IsInRole("Admin"))
            return Forbid();

        try
        {
            await _templateService.DeleteAsync(id);
            return RedirectToPage(new { message = "Template archived successfully.", success = true });
        }
        catch (Exception)
        {
            return RedirectToPage(new { message = "Failed to archive template.", success = false });
        }
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task LoadTemplatesAsync()
    {
        ChannelType? channel = null;
        if (int.TryParse(Filter.Channel, out var ch) && Enum.IsDefined(typeof(ChannelType), ch))
            channel = (ChannelType)ch;

        TemplateStatus? status = null;
        if (int.TryParse(Filter.Status, out var st) && Enum.IsDefined(typeof(TemplateStatus), st))
            status = (TemplateStatus)st;

        if (Page < 1) Page = 1;

        var result = await _templateService.GetPagedAsync(channel, status, Page, PageSize);

        Items = result.Items;
        Total = result.Total;
        TotalPages = result.TotalPages;
    }
}

/// <summary>Filter view model for template list queries.</summary>
public class TemplateFilterViewModel
{
    /// <summary>Channel filter as integer string: 1=Email, 2=Letter, 3=Sms.</summary>
    public string? Channel { get; set; }

    /// <summary>Status filter as integer string: 1=Draft, 2=Published, 3=Archived.</summary>
    public string? Status { get; set; }
}
