using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using ValidationException = CampaignEngine.Domain.Exceptions.ValidationException;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Template edit page — Designer and Admin only.
/// Channel type is read-only after creation.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
public class EditTemplateModel : PageModel
{
    private readonly ITemplateService _templateService;
    private readonly ISubTemplateResolverService _subTemplateResolver;

    public EditTemplateModel(
        ITemplateService templateService,
        ISubTemplateResolverService subTemplateResolver)
    {
        _templateService = templateService;
        _subTemplateResolver = subTemplateResolver;
    }

    [BindProperty]
    public EditTemplateInputModel Input { get; set; } = new();

    /// <summary>Template ID from route.</summary>
    public Guid TemplateId { get; private set; }

    /// <summary>Channel display string (read-only in UI).</summary>
    public string ChannelDisplay { get; private set; } = string.Empty;

    /// <summary>Status display string.</summary>
    public string StatusDisplay { get; private set; } = string.Empty;

    /// <summary>Available sub-templates for the selector panel.</summary>
    public IReadOnlyList<TemplateSummaryDto> AvailableSubTemplates { get; private set; }
        = Array.Empty<TemplateSummaryDto>();

    /// <summary>Sub-template names referenced in the current template's body path.</summary>
    public IReadOnlyList<string> ReferencedSubTemplates { get; private set; }
        = Array.Empty<string>();

    /// <summary>Version history for audit trail display (US-024). Read-only, newest first.</summary>
    public IReadOnlyList<TemplateHistoryDto> VersionHistory { get; private set; }
        = Array.Empty<TemplateHistoryDto>();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var template = await _templateService.GetByIdAsync(id);
        if (template is null) return NotFound();

        TemplateId = id;
        ChannelDisplay = template.Channel.ToString();
        StatusDisplay = template.Status.ToString();

        Input = new EditTemplateInputModel
        {
            Name = template.Name,
            BodyPath = template.BodyPath,
            Description = template.Description,
            IsSubTemplate = template.IsSubTemplate
        };

        await LoadSubTemplateDataAsync(template.BodyPath);
        VersionHistory = await _templateService.GetHistoryAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        if (!ModelState.IsValid)
        {
            // Re-populate read-only display fields
            var existing = await _templateService.GetByIdAsync(id);
            if (existing is null) return NotFound();
            TemplateId = id;
            ChannelDisplay = existing.Channel.ToString();
            StatusDisplay = existing.Status.ToString();
            await LoadSubTemplateDataAsync(Input.BodyPath);
            VersionHistory = await _templateService.GetHistoryAsync(id);
            return Page();
        }

        try
        {
            var request = new UpdateTemplateRequest
            {
                Name = Input.Name,
                BodyPath = Input.BodyPath,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description,
                IsSubTemplate = Input.IsSubTemplate
            };

            await _templateService.UpdateAsync(id, request);
            return RedirectToPage("Index", new { message = $"Template '{Input.Name}' updated successfully.", success = true });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            // Re-populate read-only display fields
            var existing = await _templateService.GetByIdAsync(id);
            TemplateId = id;
            ChannelDisplay = existing?.Channel.ToString() ?? string.Empty;
            StatusDisplay = existing?.Status.ToString() ?? string.Empty;
            await LoadSubTemplateDataAsync(Input.BodyPath);
            VersionHistory = await _templateService.GetHistoryAsync(id);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task LoadSubTemplateDataAsync(string bodyPath)
    {
        AvailableSubTemplates = await _templateService.GetSubTemplatesAsync();
        var refs = _subTemplateResolver.ExtractReferences(bodyPath ?? string.Empty);
        ReferencedSubTemplates = refs.Select(r => r.Name).ToList().AsReadOnly();
    }
}

/// <summary>Input model for template edit form.</summary>
public class EditTemplateInputModel
{
    [Required(ErrorMessage = "Template name is required.")]
    [MaxLength(200, ErrorMessage = "Name must not exceed 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Body path is required.")]
    [MaxLength(500, ErrorMessage = "Body path must not exceed 500 characters.")]
    public string BodyPath { get; set; } = string.Empty;

    /// <summary>When checked, marks this template as a reusable sub-template block.</summary>
    public bool IsSubTemplate { get; set; } = false;
}
