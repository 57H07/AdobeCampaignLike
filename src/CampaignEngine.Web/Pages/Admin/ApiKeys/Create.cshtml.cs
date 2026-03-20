using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.ApiKeys;

/// <summary>
/// Admin-only page for creating a new API key.
/// On successful creation, redirects to Index with the plaintext key in TempData.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class CreateModel : PageModel
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ICurrentUserService _currentUserService;

    [BindProperty]
    public CreateApiKeyRequest Input { get; set; } = new();

    [TempData]
    public string? NewPlaintextKey { get; set; }

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool IsSuccess { get; set; }

    public CreateModel(IApiKeyService apiKeyService, ICurrentUserService currentUserService)
    {
        _apiKeyService = apiKeyService;
        _currentUserService = currentUserService;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await _apiKeyService.CreateAsync(
            Input,
            _currentUserService.UserName ?? "admin");

        NewPlaintextKey = result.PlaintextKey;
        Message = $"API key '{result.Key.Name}' created successfully. Save the key value shown below — it will not be shown again.";
        IsSuccess = true;

        return RedirectToPage("Index");
    }
}
