using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using DomainValidationException = CampaignEngine.Domain.Exceptions.ValidationException;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Web.Pages.Admin.DataSources;

/// <summary>
/// Admin-only page to create a new data source.
/// Connection string is passed in plaintext from the UI and encrypted by the service.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class CreateModel : PageModel
{
    private readonly IDataSourceService _dataSourceService;

    public CreateModel(IDataSourceService dataSourceService)
    {
        _dataSourceService = dataSourceService;
    }

    [BindProperty]
    public CreateDataSourceViewModel Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var request = new CreateDataSourceRequest
            {
                Name = Input.Name,
                Type = Input.Type,
                ConnectionString = Input.ConnectionString,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description,
                Fields = Input.Fields
                    .Where(f => !string.IsNullOrWhiteSpace(f.FieldName))
                    .Select(f => new UpsertFieldRequest
                    {
                        FieldName = f.FieldName,
                        DataType = f.DataType,
                        IsFilterable = f.IsFilterable,
                        IsRecipientAddress = f.IsRecipientAddress,
                        Description = f.FieldDescription
                    })
                    .ToList()
            };

            var created = await _dataSourceService.CreateAsync(request);

            TempData["StatusMessage"] = $"Data source '{created.Name}' was created successfully.";
            TempData["IsSuccess"] = true;
            return RedirectToPage("Details", new { id = created.Id });
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

/// <summary>
/// View model for the Create form.
/// </summary>
public class CreateDataSourceViewModel
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public DataSourceType Type { get; set; }

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public List<FieldRowViewModel> Fields { get; set; } = [];
}

public class FieldRowViewModel
{
    public string FieldName { get; set; } = string.Empty;
    public string DataType { get; set; } = "nvarchar";
    public bool IsFilterable { get; set; } = true;
    public bool IsRecipientAddress { get; set; } = false;
    public string? FieldDescription { get; set; }
}
