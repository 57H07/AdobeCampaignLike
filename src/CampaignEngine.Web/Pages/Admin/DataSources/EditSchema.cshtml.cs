using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.DataSources;

/// <summary>
/// Admin-only page to replace the full field schema for a data source.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class EditSchemaModel : PageModel
{
    private readonly IDataSourceService _dataSourceService;

    public EditSchemaModel(IDataSourceService dataSourceService)
    {
        _dataSourceService = dataSourceService;
    }

    public Guid DataSourceId { get; private set; }

    [BindProperty]
    public List<SchemaFieldViewModel> Fields { get; set; } = [];

    public IReadOnlyList<DataTypeOption> DataTypes { get; } = new List<DataTypeOption>
    {
        new("nvarchar",        "Text (nvarchar)"),
        new("int",             "Integer (int)"),
        new("datetime",        "Date/Time"),
        new("bit",             "Boolean (bit)"),
        new("decimal",         "Decimal"),
        new("uniqueidentifier","GUID"),
        new("bigint",          "Long (bigint)"),
        new("float",           "Float"),
        new("date",            "Date"),
        new("time",            "Time")
    };

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        DataSourceId = id;
        var ds = await _dataSourceService.GetByIdAsync(id);
        if (ds is null) return NotFound();

        Fields = ds.Fields.Select(f => new SchemaFieldViewModel
        {
            FieldName = f.FieldName,
            DataType = f.DataType,
            IsFilterable = f.IsFilterable,
            IsRecipientAddress = f.IsRecipientAddress
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        DataSourceId = id;

        if (!ModelState.IsValid)
            return Page();

        try
        {
            var validFields = Fields
                .Where(f => !string.IsNullOrWhiteSpace(f.FieldName))
                .Select(f => new UpsertFieldRequest
                {
                    FieldName = f.FieldName.Trim(),
                    DataType = f.DataType,
                    IsFilterable = f.IsFilterable,
                    IsRecipientAddress = f.IsRecipientAddress
                })
                .ToList();

            await _dataSourceService.UpdateSchemaAsync(id, validFields);

            TempData["StatusMessage"] = $"Schema updated successfully with {validFields.Count} field(s).";
            TempData["IsSuccess"] = true;
            return RedirectToPage("Details", new { id });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Failed to update schema: {ex.Message}");
            return Page();
        }
    }
}

public class SchemaFieldViewModel
{
    public string FieldName { get; set; } = string.Empty;
    public string DataType { get; set; } = "nvarchar";
    public bool IsFilterable { get; set; } = true;
    public bool IsRecipientAddress { get; set; } = false;
}

public record DataTypeOption(string Value, string Label);
