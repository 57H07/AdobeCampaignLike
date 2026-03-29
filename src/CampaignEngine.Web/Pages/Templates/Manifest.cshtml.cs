using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Placeholder manifest editor page — Designer and Admin only.
/// Allows declaring typed placeholder entries with source indication.
/// Supports auto-detection of placeholders from template HTML.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
public class TemplateManifestModel : PageModel
{
    private readonly ITemplateService _templateService;
    private readonly IPlaceholderManifestService _manifestService;
    private readonly IPlaceholderParserService _parserService;

    public TemplateManifestModel(
        ITemplateService templateService,
        IPlaceholderManifestService manifestService,
        IPlaceholderParserService parserService)
    {
        _templateService = templateService;
        _manifestService = manifestService;
        _parserService = parserService;
    }

    public Guid TemplateId { get; private set; }
    public string TemplateName { get; private set; } = string.Empty;
    public ManifestValidationResult? ValidationResult { get; private set; }
    public string? SuccessMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public List<ManifestEntryInputModel> Entries { get; set; } = new();

    public IReadOnlyList<(string Value, string Text)> PlaceholderTypeOptions { get; } = new List<(string, string)>
    {
        ("1", "Scalar"),
        ("2", "Table"),
        ("3", "List"),
        ("4", "FreeField")
    }.AsReadOnly();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var template = await _templateService.GetByIdAsync(id);
        if (template is null) return NotFound();

        TemplateId = id;
        TemplateName = template.Name;

        var entries = await _manifestService.GetByTemplateIdAsync(id);
        ValidationResult = _parserService.ValidateManifestCompleteness(template.BodyPath, entries);

        Entries = entries.Select(e => new ManifestEntryInputModel
        {
            Id = e.Id,
            Key = e.Key,
            Type = e.Type == "Scalar"    ? PlaceholderType.Scalar
                 : e.Type == "Table"     ? PlaceholderType.Table
                 : e.Type == "List"      ? PlaceholderType.List
                 : e.Type == "FreeField" ? PlaceholderType.FreeField
                 : PlaceholderType.Scalar,
            IsFromDataSource = e.IsFromDataSource,
            Description = e.Description
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        var template = await _templateService.GetByIdAsync(id);
        if (template is null) return NotFound();

        TemplateId = id;
        TemplateName = template.Name;

        if (!ModelState.IsValid)
        {
            var current = await _manifestService.GetByTemplateIdAsync(id);
            ValidationResult = _parserService.ValidateManifestCompleteness(template.BodyPath, current);
            return Page();
        }

        try
        {
            var requests = Entries.Select(e => new UpsertPlaceholderManifestRequest
            {
                Key = e.Key,
                Type = e.Type,
                IsFromDataSource = e.IsFromDataSource,
                Description = string.IsNullOrWhiteSpace(e.Description) ? null : e.Description
            }).ToList();

            var saved = await _manifestService.ReplaceManifestAsync(id, requests);
            ValidationResult = _parserService.ValidateManifestCompleteness(template.BodyPath, saved);

            SuccessMessage = $"Placeholder manifest saved. {saved.Count} entr{(saved.Count == 1 ? "y" : "ies")} declared.";

            // Reload Entries to reflect saved state
            Entries = saved.Select(e => new ManifestEntryInputModel
            {
                Id = e.Id,
                Key = e.Key,
                Type = e.Type == "Scalar"    ? PlaceholderType.Scalar
                     : e.Type == "Table"     ? PlaceholderType.Table
                     : e.Type == "List"      ? PlaceholderType.List
                     : e.Type == "FreeField" ? PlaceholderType.FreeField
                     : PlaceholderType.Scalar,
                IsFromDataSource = e.IsFromDataSource,
                Description = e.Description
            }).ToList();

            return Page();
        }
        catch (CampaignEngine.Domain.Exceptions.ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var current = await _manifestService.GetByTemplateIdAsync(id);
            ValidationResult = _parserService.ValidateManifestCompleteness(template.BodyPath, current);
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save manifest: {ex.Message}";
            var current = await _manifestService.GetByTemplateIdAsync(id);
            ValidationResult = _parserService.ValidateManifestCompleteness(template.BodyPath, current);
            return Page();
        }
    }
}

/// <summary>
/// Input model for a single manifest entry in the editor form.
/// </summary>
public class ManifestEntryInputModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Placeholder key is required.")]
    [MaxLength(100, ErrorMessage = "Key must not exceed 100 characters.")]
    [RegularExpression(@"^[a-zA-Z_][a-zA-Z0-9_]*$",
        ErrorMessage = "Key must start with a letter or underscore and contain only alphanumeric characters and underscores.")]
    public string Key { get; set; } = string.Empty;

    [Required]
    public PlaceholderType Type { get; set; } = PlaceholderType.Scalar;

    public bool IsFromDataSource { get; set; } = true;

    [MaxLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; set; }
}
