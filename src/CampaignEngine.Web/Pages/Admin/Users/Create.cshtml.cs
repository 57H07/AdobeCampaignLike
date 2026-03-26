using CampaignEngine.Application.DTOs.Identity;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Web.ViewModels.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.Users;

/// <summary>
/// Admin-only page for creating new application users with a role assignment.
/// </summary>
[Authorize(Policy = CampaignEngine.Application.DependencyInjection.AuthorizationPolicies.RequireAdmin)]
public class CreateModel : PageModel
{
    private readonly IIdentityService _identityService;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    [BindProperty]
    public CreateUserViewModel Input { get; set; } = new();

    public CreateModel(
        IIdentityService identityService,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _identityService = identityService;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Validate and default the role (default: Operator per business rule 4)
        var role = CampaignEngine.Domain.Enums.UserRole.All.Contains(Input.Role)
            ? Input.Role
            : CampaignEngine.Domain.Enums.UserRole.Default;

        var result = await _identityService.CreateUserAsync(new CreateUserRequest
        {
            UserName = Input.UserName,
            Email = Input.Email,
            DisplayName = Input.DisplayName,
            Password = Input.Password,
            Role = role
        });

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        await _auditService.LogUserCreatedAsync(
            _currentUserService.UserId ?? "system",
            _currentUserService.UserName ?? "system",
            result.UserId!,
            result.UserName ?? Input.UserName);

        return RedirectToPage("Index", new { message = $"User '{Input.UserName}' created with role '{role}'.", success = true });
    }
}
