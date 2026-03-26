using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Account;

/// <summary>
/// Logout page: signs the user out and records the event.
/// </summary>
[AllowAnonymous]
public class LogoutModel : PageModel
{
    private readonly IIdentityService _identityService;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    public LogoutModel(
        IIdentityService identityService,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _identityService = identityService;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = _currentUserService.UserId;
        var userName = _currentUserService.UserName;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        await _identityService.SignOutAsync();

        if (userId != null && userName != null)
        {
            await _auditService.LogLogoutAsync(userId, userName, ipAddress);
        }

        return Page();
    }
}
