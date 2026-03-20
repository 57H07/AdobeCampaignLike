using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Template detail page — read-only view accessible to all authenticated users.
/// Shows all metadata, HTML body, and placeholder manifest.
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

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
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
}
