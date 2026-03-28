using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.ApiKeys;

/// <summary>
/// Admin-only page for updating the rate limit of an existing API key.
/// Allows admins to change the per-key rate limit after key creation.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class EditRateLimitModel : PageModel
{
    private readonly IApiKeyService _apiKeyService;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public ApiKeyDto? ApiKey { get; set; }

    [BindProperty]
    public UpdateApiKeyRateLimitRequest Input { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool IsSuccess { get; set; }

    public EditRateLimitModel(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        ApiKey = await _apiKeyService.GetByIdAsync(Id);
        if (ApiKey is null)
            return NotFound();

        // Pre-populate the input with the current rate limit value.
        Input = new UpdateApiKeyRateLimitRequest
        {
            RateLimitPerMinute = ApiKey.RateLimitPerMinute
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ApiKey = await _apiKeyService.GetByIdAsync(Id);
        if (ApiKey is null)
            return NotFound();

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await _apiKeyService.UpdateRateLimitAsync(Id, Input);
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        Message = $"Rate limit for '{ApiKey.Name}' updated successfully.";
        IsSuccess = true;
        return RedirectToPage("Index");
    }
}
