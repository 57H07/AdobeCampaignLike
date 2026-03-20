using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.ApiKeys;

/// <summary>
/// Admin-only page listing all API keys with revoke and rotate actions.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class IndexModel : PageModel
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ICurrentUserService _currentUserService;

    public List<ApiKeyDto> ApiKeys { get; private set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Plaintext key returned from a create or rotate operation.
    /// Stored in TempData so it survives the redirect and is shown once.
    /// </summary>
    [TempData]
    public string? NewPlaintextKey { get; set; }

    public IndexModel(IApiKeyService apiKeyService, ICurrentUserService currentUserService)
    {
        _apiKeyService = apiKeyService;
        _currentUserService = currentUserService;
    }

    public async Task OnGetAsync()
    {
        ApiKeys = (await _apiKeyService.GetAllAsync()).ToList();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id)
    {
        await _apiKeyService.RevokeAsync(id);

        Message = "API key revoked successfully.";
        IsSuccess = true;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRotateAsync(Guid id)
    {
        var result = await _apiKeyService.RotateAsync(
            id,
            _currentUserService.UserName ?? "admin");

        NewPlaintextKey = result.PlaintextKey;
        Message = $"API key '{result.Key.Name}' created. Save the key value shown below — it will not be shown again.";
        IsSuccess = true;
        return RedirectToPage();
    }
}
