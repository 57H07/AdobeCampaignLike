using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Identity;
using CampaignEngine.Web.ViewModels.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.Users;

/// <summary>
/// Admin-only page listing all users and providing activate/deactivate actions.
/// </summary>
[Authorize(Policy = CampaignEngine.Application.DependencyInjection.AuthorizationPolicies.RequireAdmin)]
public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    public UserListViewModel ViewModel { get; private set; } = new();

    public IndexModel(
        UserManager<ApplicationUser> userManager,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    public async Task OnGetAsync(string? message = null, bool success = false)
    {
        var users = _userManager.Users.ToList();
        var userSummaries = new List<UserSummaryViewModel>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userSummaries.Add(new UserSummaryViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                Roles = roles.ToList()
            });
        }

        ViewModel = new UserListViewModel
        {
            Users = userSummaries,
            Message = message,
            IsSuccess = success
        };
    }

    public async Task<IActionResult> OnPostDeactivateAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        user.IsActive = false;
        await _userManager.UpdateAsync(user);

        await _auditService.LogEventAsync(
            AuthEventType.UserDeactivated,
            _currentUserService.UserId,
            _currentUserService.UserName,
            $"User '{user.UserName}' deactivated by admin '{_currentUserService.UserName}'",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            succeeded: true);

        return RedirectToPage(new { message = $"User '{user.UserName}' has been deactivated.", success = true });
    }

    public async Task<IActionResult> OnPostActivateAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        user.IsActive = true;
        await _userManager.UpdateAsync(user);

        await _auditService.LogEventAsync(
            AuthEventType.UserReactivated,
            _currentUserService.UserId,
            _currentUserService.UserName,
            $"User '{user.UserName}' reactivated by admin '{_currentUserService.UserName}'",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            succeeded: true);

        return RedirectToPage(new { message = $"User '{user.UserName}' has been activated.", success = true });
    }
}
