using CampaignEngine.Application.DTOs.Identity;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the database with a default admin user on first deployment.
/// Runs on every application startup but is fully idempotent:
/// if an admin user already exists the method returns immediately without
/// creating or modifying any records.
/// </summary>
public class AdminSeeder
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly DefaultAdminOptions _options;
    private readonly IAppLogger<AdminSeeder> _logger;

    // Default credentials shipped in appsettings.json — used to detect unchanged defaults.
    private const string DefaultUserName = "admin";
    private const string DefaultEmail = "admin@campaignengine.local";
    private const string DefaultPassword = "Admin@1234!";

    public AdminSeeder(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<DefaultAdminOptions> options,
        IAppLogger<AdminSeeder> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ensures all application roles exist, then seeds the default admin account if no
    /// user with the Admin role currently exists.  Idempotent — safe to call on every startup.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRolesAsync(cancellationToken);
        await EnsureDefaultAdminAsync(cancellationToken);
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in UserRole.All)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
                continue;

            _logger.LogInformation("Creating role {RoleName}", roleName);
            var role = new ApplicationRole(roleName);
            var result = await _roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogError(
                    new InvalidOperationException(errors),
                    "Failed to create role {RoleName}: {Errors}",
                    roleName,
                    errors);
            }
        }
    }

    private async Task EnsureDefaultAdminAsync(CancellationToken cancellationToken)
    {
        // Check if any admin user already exists — if so, skip seeding.
        var admins = await _userManager.GetUsersInRoleAsync(UserRole.Admin);
        if (admins.Count > 0)
        {
            _logger.LogDebug("Admin user(s) already exist — skipping default admin seeding.");
            return;
        }

        _logger.LogInformation(
            "No admin user found. Creating default admin account: {UserName} <{Email}>",
            _options.UserName,
            _options.Email);

        var user = new ApplicationUser
        {
            UserName = _options.UserName,
            Email = _options.Email,
            DisplayName = _options.DisplayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, _options.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            _logger.LogError(
                new InvalidOperationException(errors),
                "Failed to create default admin user {UserName}: {Errors}",
                _options.UserName,
                errors);
            return;
        }

        var addRoleResult = await _userManager.AddToRoleAsync(user, UserRole.Admin);
        if (!addRoleResult.Succeeded)
        {
            var errors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
            _logger.LogError(
                new InvalidOperationException(errors),
                "Failed to assign Admin role to user {UserName}: {Errors}",
                _options.UserName,
                errors);
            return;
        }

        _logger.LogInformation(
            "Default admin user created successfully (UserId={UserId}).",
            user.Id);

        // Warn if the account is still using the well-known default credentials.
        WarnIfDefaultCredentials();
    }

    private void WarnIfDefaultCredentials()
    {
        bool isDefaultUserName = string.Equals(
            _options.UserName, DefaultUserName, StringComparison.OrdinalIgnoreCase);
        bool isDefaultEmail = string.Equals(
            _options.Email, DefaultEmail, StringComparison.OrdinalIgnoreCase);
        bool isDefaultPassword = string.Equals(
            _options.Password, DefaultPassword, StringComparison.Ordinal);

        if (isDefaultUserName || isDefaultEmail || isDefaultPassword)
        {
            _logger.LogWarning(
                "SECURITY WARNING: The default admin account is using well-known default " +
                "credentials (username='{UserName}', email='{Email}'). " +
                "Change these immediately via the Users management page or by updating " +
                "'DefaultAdmin' in appsettings.json / environment variables.",
                _options.UserName,
                _options.Email);
        }
    }
}
