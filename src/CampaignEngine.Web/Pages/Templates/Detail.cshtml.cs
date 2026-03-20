using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Template detail page — read-only view accessible to all authenticated users.
/// Shows all metadata, HTML body, and placeholder manifest.
/// Status transition actions (Publish, Archive) restricted to Designer and Admin roles.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
public class TemplateDetailModel : PageModel
{
    private readonly ITemplateService _templateService;
    private readonly IPlaceholderManifestService _manifestService;
    private readonly IPlaceholderParserService _parserService;

    public TemplateDetailModel(
        ITemplateService templateService,
        IPlaceholderManifestService manifestService,
        IPlaceholderParserService parserService)
    {
        _templateService = templateService;
        _manifestService = manifestService;
        _parserService = parserService;
    }

    public TemplateDto? Template { get; private set; }
    public ManifestValidationResult? ValidationResult { get; private set; }
    public string? StatusMessage { get; set; }
    public bool IsSuccess { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? message = null, bool success = false)
    {
        StatusMessage = message;
        IsSuccess = success;

        var template = await _templateService.GetByIdAsync(id);
        if (template is null) return NotFound();

        var manifests = await _manifestService.GetByTemplateIdAsync(id);

        Template = new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            HtmlBody = template.HtmlBody,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            PlaceholderManifests = manifests
        };

        ValidationResult = _parserService.ValidateManifestCompleteness(template.HtmlBody, manifests);

        return Page();
    }

    /// <summary>
    /// Publishes a Draft template (transitions Draft → Published).
    /// Restricted to Designer and Admin roles.
    /// </summary>
    public async Task<IActionResult> OnPostPublishAsync(Guid id)
    {
        if (!User.IsInRole("Designer") && !User.IsInRole("Admin"))
            return Forbid();

        try
        {
            await _templateService.PublishAsync(id);
            return RedirectToPage(new { id, message = "Template published successfully.", success = true });
        }
        catch (ValidationException ex)
        {
            return RedirectToPage(new { id, message = ex.Message, success = false });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Archives a template (transitions Draft or Published → Archived).
    /// Restricted to Designer and Admin roles.
    /// </summary>
    public async Task<IActionResult> OnPostArchiveAsync(Guid id)
    {
        if (!User.IsInRole("Designer") && !User.IsInRole("Admin"))
            return Forbid();

        try
        {
            await _templateService.ArchiveAsync(id);
            return RedirectToPage(new { id, message = "Template archived successfully.", success = true });
        }
        catch (ValidationException ex)
        {
            return RedirectToPage(new { id, message = ex.Message, success = false });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }
}
