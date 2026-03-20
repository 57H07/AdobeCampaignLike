using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using DomainValidationException = CampaignEngine.Domain.Exceptions.ValidationException;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Web.Pages.Admin.DataSources;

/// <summary>
/// Admin-only page to edit an existing data source.
/// Connection string is optional — leaving it blank preserves the encrypted stored value.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class EditModel : PageModel
{
    private readonly IDataSourceService _dataSourceService;

    public EditModel(IDataSourceService dataSourceService)
    {
        _dataSourceService = dataSourceService;
    }

    public Guid Id { get; private set; }

    [BindProperty]
    public EditDataSourceViewModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Id = id;
        var ds = await _dataSourceService.GetByIdAsync(id);
        if (ds is null) return NotFound();

        Input = new EditDataSourceViewModel
        {
            Name = ds.Name,
            Type = ds.Type,
            Description = ds.Description
            // ConnectionString intentionally left blank
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        Id = id;

        if (!ModelState.IsValid)
            return Page();

        try
        {
            var request = new UpdateDataSourceRequest
            {
                Name = Input.Name,
                Type = Input.Type,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description,
                ConnectionString = string.IsNullOrWhiteSpace(Input.ConnectionString) ? null : Input.ConnectionString
            };

            var updated = await _dataSourceService.UpdateAsync(id, request);

            TempData["StatusMessage"] = $"Data source '{updated.Name}' was updated successfully.";
            TempData["IsSuccess"] = true;
            return RedirectToPage("Details", new { id });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (DomainValidationException ex)
        {
            foreach (var kvp in ex.Errors)
            foreach (var error in kvp.Value)
                ModelState.AddModelError(kvp.Key, error);

            return Page();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
            return Page();
        }
    }
}

public class EditDataSourceViewModel
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public DataSourceType Type { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Optional — leave blank to preserve stored encrypted string.</summary>
    public string? ConnectionString { get; set; }
}
