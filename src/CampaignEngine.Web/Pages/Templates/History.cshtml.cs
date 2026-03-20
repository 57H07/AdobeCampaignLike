using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Templates;

/// <summary>
/// Version history page for a template.
/// Displays all historic snapshots ordered by version descending.
/// Supports inline diff view between any two versions.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
public class TemplateHistoryModel : PageModel
{
    private readonly ITemplateService _templateService;

    public TemplateHistoryModel(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    // ----------------------------------------------------------------
    // Properties
    // ----------------------------------------------------------------

    public TemplateDto? Template { get; private set; }
    public IReadOnlyList<TemplateHistoryDto> History { get; private set; } = Array.Empty<TemplateHistoryDto>();
    public TemplateDiffDto? Diff { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int? DiffFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DiffTo { get; set; }

    public string? ErrorMessage { get; private set; }

    // ----------------------------------------------------------------
    // Handlers
    // ----------------------------------------------------------------

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null)
            return NotFound();

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

        History = await _templateService.GetHistoryAsync(id, cancellationToken);

        // Load diff if version parameters are provided
        if (DiffFrom.HasValue && DiffFrom.Value >= 1)
        {
            try
            {
                Diff = await _templateService.GetDiffAsync(id, DiffFrom.Value, DiffTo, cancellationToken);
            }
            catch (NotFoundException ex)
            {
                ErrorMessage = ex.Message;
            }
        }

        return Page();
    }

    /// <summary>
    /// Handles the revert POST action from the revert confirmation dialog.
    /// Reverts template to the requested version and redirects back to history.
    /// </summary>
    public async Task<IActionResult> OnPostRevertAsync(
        Guid id,
        int version,
        CancellationToken cancellationToken = default)
    {
        if (!User.IsInRole("Designer") && !User.IsInRole("Admin"))
            return Forbid();

        if (version < 1)
            return BadRequest("Version must be >= 1.");

        try
        {
            await _templateService.RevertToVersionAsync(id, version, User.Identity?.Name, cancellationToken);
            return RedirectToPage(new
            {
                id,
                message = $"Template reverted to version {version}. A new version has been created.",
                success = true
            });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception)
        {
            return RedirectToPage(new
            {
                id,
                message = "Failed to revert template. Please try again.",
                success = false
            });
        }
    }
}
