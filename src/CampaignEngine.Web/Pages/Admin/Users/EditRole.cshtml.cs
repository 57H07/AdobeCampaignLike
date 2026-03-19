using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Identity;
using CampaignEngine.Web.ViewModels.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.Users;

/// <summary>
/// Admin-only page for assigning a role to a user.
/// Replaces all existing roles with the selected one (single-role model).
/// </summary>
[Authorize(Policy = CampaignEngine.Application.DependencyInjection.AuthorizationPolicies.RequireAdmin)]
public class EditRoleModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    [BindProperty]
    public EditUserRoleViewModel Input { get; set; } = new();

    public EditRoleModel(
        UserManager<ApplicationUser> userManager,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);

        Input = new EditUserRoleViewModel
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            CurrentRoles = roles.ToList(),
            SelectedRole = roles.FirstOrDefault() ?? CampaignEngine.Domain.Enums.UserRole.Default
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _userManager.FindByIdAsync(Input.UserId);
        if (user == null) return NotFound();

        // Validate the selected role is one of the known roles
        if (!CampaignEngine.Domain.Enums.UserRole.All.Contains(Input.SelectedRole))
        {
            ModelState.AddModelError(nameof(Input.SelectedRole), "Invalid role selected.");
            return Page();
        }

        // Remove all existing roles, assign selected role
        var existingRoles = await _userManager.GetRolesAsync(user);
        foreach (var existingRole in existingRoles)
        {
            await _userManager.RemoveFromRoleAsync(user, existingRole);
            await _auditService.LogRoleRemovedAsync(
                _currentUserService.UserId ?? "system",
                _currentUserService.UserName ?? "system",
                user.Id,
                user.UserName ?? user.Email ?? user.Id,
                existingRole);
        }

        await _userManager.AddToRoleAsync(user, Input.SelectedRole);

        await _auditService.LogRoleAssignedAsync(
            _currentUserService.UserId ?? "system",
            _currentUserService.UserName ?? "system",
            user.Id,
            user.UserName ?? user.Email ?? user.Id,
            Input.SelectedRole);

        return RedirectToPage("Index", new
        {
            message = $"Role '{Input.SelectedRole}' assigned to user '{user.UserName}'.",
            success = true
        });
    }
}
