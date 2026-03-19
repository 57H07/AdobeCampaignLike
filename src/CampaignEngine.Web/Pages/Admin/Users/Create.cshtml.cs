using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Infrastructure.Identity;
using CampaignEngine.Web.ViewModels.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.Users;

/// <summary>
/// Admin-only page for creating new application users with a role assignment.
/// </summary>
[Authorize(Policy = CampaignEngine.Application.DependencyInjection.AuthorizationPolicies.RequireAdmin)]
public class CreateModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    [BindProperty]
    public CreateUserViewModel Input { get; set; } = new();

    public CreateModel(
        UserManager<ApplicationUser> userManager,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = new ApplicationUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            DisplayName = Input.DisplayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        // Assign the selected role (default: Operator per business rule 4)
        var role = CampaignEngine.Domain.Enums.UserRole.All.Contains(Input.Role)
            ? Input.Role
            : CampaignEngine.Domain.Enums.UserRole.Default;

        await _userManager.AddToRoleAsync(user, role);

        await _auditService.LogUserCreatedAsync(
            _currentUserService.UserId ?? "system",
            _currentUserService.UserName ?? "system",
            user.Id,
            user.UserName ?? user.Email);

        return RedirectToPage("Index", new { message = $"User '{user.UserName}' created with role '{role}'.", success = true });
    }
}
