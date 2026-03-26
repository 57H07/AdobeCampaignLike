using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Web.ViewModels.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Admin.Users;

/// <summary>
/// Admin-only page listing all users and providing activate/deactivate actions.
/// </summary>
[Authorize(Policy = CampaignEngine.Application.DependencyInjection.AuthorizationPolicies.RequireAdmin)]
public class IndexModel : PageModel
{
    private readonly IIdentityService _identityService;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    public UserListViewModel ViewModel { get; private set; } = new();

    public IndexModel(
        IIdentityService identityService,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _identityService = identityService;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    public async Task OnGetAsync(string? message = null, bool success = false)
    {
        var users = await _identityService.GetAllUsersAsync();
        var userSummaries = new List<UserSummaryViewModel>();

        foreach (var user in users)
        {
            userSummaries.Add(new UserSummaryViewModel
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                Roles = user.Roles.ToList()
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
        var result = await _identityService.SetUserActiveStatusAsync(id, isActive: false);
        if (result == null) return NotFound();

        var (_, userName) = result.Value;

        await _auditService.LogEventAsync(
            AuthEventType.UserDeactivated,
            _currentUserService.UserId,
            _currentUserService.UserName,
            $"User '{userName}' deactivated by admin '{_currentUserService.UserName}'",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            succeeded: true);

        return RedirectToPage(new { message = $"User '{userName}' has been deactivated.", success = true });
    }

    public async Task<IActionResult> OnPostActivateAsync(string id)
    {
        var result = await _identityService.SetUserActiveStatusAsync(id, isActive: true);
        if (result == null) return NotFound();

        var (_, userName) = result.Value;

        await _auditService.LogEventAsync(
            AuthEventType.UserReactivated,
            _currentUserService.UserId,
            _currentUserService.UserName,
            $"User '{userName}' reactivated by admin '{_currentUserService.UserName}'",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            succeeded: true);

        return RedirectToPage(new { message = $"User '{userName}' has been activated.", success = true });
    }
}
