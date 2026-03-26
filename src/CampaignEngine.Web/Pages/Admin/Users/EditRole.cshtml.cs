using CampaignEngine.Application.Interfaces;
using CampaignEngine.Web.ViewModels.UserManagement;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IIdentityService _identityService;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    [BindProperty]
    public EditUserRoleViewModel Input { get; set; } = new();

    public EditRoleModel(
        IIdentityService identityService,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _identityService = identityService;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await _identityService.GetUserByIdAsync(id);
        if (user == null) return NotFound();

        Input = new EditUserRoleViewModel
        {
            UserId = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            CurrentRoles = user.Roles.ToList(),
            SelectedRole = user.Roles.FirstOrDefault() ?? CampaignEngine.Domain.Enums.UserRole.Default
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Validate the selected role is one of the known roles
        if (!CampaignEngine.Domain.Enums.UserRole.All.Contains(Input.SelectedRole))
        {
            ModelState.AddModelError(nameof(Input.SelectedRole), "Invalid role selected.");
            return Page();
        }

        // Get the user's current roles before reassignment for audit logging
        var existingUser = await _identityService.GetUserByIdAsync(Input.UserId);
        if (existingUser == null) return NotFound();

        var previousRoles = existingUser.Roles;

        var result = await _identityService.AssignRoleAsync(Input.UserId, Input.SelectedRole);
        if (result == null) return NotFound();

        var (userId, userName) = result.Value;
        var adminUserId = _currentUserService.UserId ?? "system";
        var adminUserName = _currentUserService.UserName ?? "system";

        foreach (var existingRole in previousRoles)
        {
            await _auditService.LogRoleRemovedAsync(
                adminUserId, adminUserName, userId, userName, existingRole);
        }

        await _auditService.LogRoleAssignedAsync(
            adminUserId, adminUserName, userId, userName, Input.SelectedRole);

        return RedirectToPage("Index", new
        {
            message = $"Role '{Input.SelectedRole}' assigned to user '{userName}'.",
            success = true
        });
    }
}
