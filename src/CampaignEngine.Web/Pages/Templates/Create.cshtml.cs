using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        var channel = (ChannelType)Input.Channel;

        // Server-side conditional validation:
        // - Email/SMS require a BodyPath
        // - Letter requires a DOCX file upload
        if (channel == ChannelType.Letter)
        {
            ModelState.Remove(nameof(Input) + "." + nameof(Input.BodyPath));

            if (Input.DocxFile == null || Input.DocxFile.Length == 0)
            {
                ModelState.AddModelError(
                    nameof(Input) + "." + nameof(Input.DocxFile),
                    "A .docx file is required for Letter templates.");
            }
            else if (!Input.DocxFile.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(
                    nameof(Input) + "." + nameof(Input.DocxFile),
                    "Only .docx files are accepted for Letter templates.");
            }
        }
        else
        {
            // BodyPath is required for Email/SMS.
            ModelState.Remove(nameof(Input) + "." + nameof(Input.DocxFile));
            if (string.IsNullOrWhiteSpace(Input.BodyPath))
            {
                ModelState.AddModelError(
                    nameof(Input) + "." + nameof(Input.BodyPath),
                    "Body path is required.");
            }
        }

        if (!ModelState.IsValid)
            return Page();

        if (!Enum.IsDefined(typeof(ChannelType), Input.Channel))
        {
            ModelState.AddModelError(nameof(Input.Channel), "Please select a valid channel.");
            return Page();
        }

        try
        {
            // Derive BodyPath for Letter from the uploaded file name.
            var bodyPath = channel == ChannelType.Letter
                ? $"templates/letter/{Input.DocxFile!.FileName}"
                : Input.BodyPath ?? string.Empty;

            var request = new CreateTemplateRequest
            {
                Name = Input.Name,
                Channel = channel,
                BodyPath = bodyPath,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description,
                IsSubTemplate = Input.IsSubTemplate,
                FileSizeBytes = Input.DocxFile?.Length
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
    public int Channel { get; set; } = (int)ChannelType.Email;

    [MaxLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; set; }

    /// <summary>
    /// Required for Email and SMS channels. For Letter, the file upload path is derived
    /// from the uploaded DOCX file and this field is ignored.
    /// </summary>
    [MaxLength(500, ErrorMessage = "Body path must not exceed 500 characters.")]
    public string? BodyPath { get; set; }

    /// <summary>
    /// DOCX file upload — required when Channel is Letter (ChannelType.Letter = 2).
    /// </summary>
    public IFormFile? DocxFile { get; set; }

    /// <summary>
    /// When checked, this template is a reusable sub-template block
    /// embeddable in parent templates via {{> name}} syntax.
    /// </summary>
    public bool IsSubTemplate { get; set; } = false;
}
