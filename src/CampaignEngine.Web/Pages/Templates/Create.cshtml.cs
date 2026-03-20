using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Template creation page — Designer and Admin only.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
public class CreateTemplateModel : PageModel
{
    private readonly ITemplateService _templateService;

    public CreateTemplateModel(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    [BindProperty]
    public CreateTemplateInputModel Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (!Enum.IsDefined(typeof(ChannelType), Input.Channel))
        {
            ModelState.AddModelError(nameof(Input.Channel), "Please select a valid channel.");
            return Page();
        }

        try
        {
            var request = new CreateTemplateRequest
            {
                Name = Input.Name,
                Channel = (ChannelType)Input.Channel,
                HtmlBody = Input.HtmlBody,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description
            };

            var template = await _templateService.CreateAsync(request);
            return RedirectToPage("Index", new { message = $"Template '{template.Name}' created successfully.", success = true });
        }
        catch (CampaignEngine.Domain.Exceptions.ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}

/// <summary>Input model for template creation form.</summary>
public class CreateTemplateInputModel
{
    [Required(ErrorMessage = "Template name is required.")]
    [MaxLength(200, ErrorMessage = "Name must not exceed 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a channel.")]
    [Range(1, 3, ErrorMessage = "Please select a valid channel.")]
    public int Channel { get; set; }

    [MaxLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "HTML body is required.")]
    public string HtmlBody { get; set; } = string.Empty;
}
