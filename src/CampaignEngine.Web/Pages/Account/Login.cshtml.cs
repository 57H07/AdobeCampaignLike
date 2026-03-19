using System.ComponentModel.DataAnnotations;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Account;

/// <summary>
/// Login page using ASP.NET Core Identity cookie authentication.
/// Records successful and failed login attempts to the auth audit log.
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthAuditService _auditService;

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }
    public string? ErrorMessage { get; private set; }

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuthAuditService auditService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditService = auditService;
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid) return Page();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _signInManager.PasswordSignInAsync(
            Input.UserName,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(Input.UserName)
                       ?? await _userManager.FindByEmailAsync(Input.UserName);

            if (user != null)
            {
                // Update last login timestamp
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);

                await _auditService.LogLoginAsync(user.Id, user.UserName ?? user.Email ?? user.Id, ipAddress);
            }

            return LocalRedirect(returnUrl);
        }

        if (result.IsLockedOut)
        {
            await _auditService.LogLoginFailedAsync(Input.UserName, ipAddress);
            ErrorMessage = "Account locked due to too many failed attempts. Try again in 15 minutes.";
            return Page();
        }

        await _auditService.LogLoginFailedAsync(Input.UserName, ipAddress);
        ErrorMessage = "Invalid username or password.";
        return Page();
    }

    public class LoginInputModel
    {
        [Required]
        [Display(Name = "Username or Email")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
