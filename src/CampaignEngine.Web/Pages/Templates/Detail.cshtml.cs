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

    public TemplateDetailModel(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    public TemplateDto? Template { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var template = await _templateService.GetByIdAsync(id);
        if (template is null) return NotFound();

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
            UpdatedAt = template.UpdatedAt
        };

        return Page();
    }
}
