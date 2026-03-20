using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Template edit page — Designer and Admin only.
/// Channel type is read-only after creation.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
public class EditTemplateModel : PageModel
{
    private readonly ITemplateService _templateService;

    public EditTemplateModel(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    [BindProperty]
    public EditTemplateInputModel Input { get; set; } = new();

    /// <summary>Template ID from route.</summary>
    public Guid TemplateId { get; private set; }

    /// <summary>Channel display string (read-only in UI).</summary>
    public string ChannelDisplay { get; private set; } = string.Empty;

    /// <summary>Status display string.</summary>
    public string StatusDisplay { get; private set; } = string.Empty;

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
            HtmlBody = template.HtmlBody,
            Description = template.Description
        };

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
            return Page();
        }

        try
        {
            var request = new UpdateTemplateRequest
            {
                Name = Input.Name,
                HtmlBody = Input.HtmlBody,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description
            };

            await _templateService.UpdateAsync(id, request);
            return RedirectToPage("Index", new { message = $"Template '{Input.Name}' updated successfully.", success = true });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (CampaignEngine.Domain.Exceptions.ValidationException ex)
        {
            // Re-populate read-only display fields
            var existing = await _templateService.GetByIdAsync(id);
            TemplateId = id;
            ChannelDisplay = existing?.Channel.ToString() ?? string.Empty;
            StatusDisplay = existing?.Status.ToString() ?? string.Empty;
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
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

    [Required(ErrorMessage = "HTML body is required.")]
    public string HtmlBody { get; set; } = string.Empty;
}
