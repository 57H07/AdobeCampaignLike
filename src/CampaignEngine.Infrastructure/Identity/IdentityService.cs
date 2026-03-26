using CampaignEngine.Application.DTOs.Identity;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Identity;

/// <summary>
/// Implements <see cref="IIdentityService"/> by wrapping ASP.NET Core Identity managers.
/// Keeps all Identity-specific types (ApplicationUser, ApplicationRole, UserManager, SignInManager)
/// inside the Infrastructure layer so the Web layer only depends on Application interfaces.
/// </summary>
public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserDto>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var users = await _userManager.Users.ToListAsync(ct);
        var result = new List<UserDto>(users.Count);

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(MapToDto(user, roles));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<UserDto?> GetUserByIdAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return MapToDto(user, roles);
    }

    /// <inheritdoc />
    public async Task<(string UserId, string UserName)?> AssignRoleAsync(
        string userId, string role, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var existingRoles = await _userManager.GetRolesAsync(user);
        foreach (var existingRole in existingRoles)
        {
            await _userManager.RemoveFromRoleAsync(user, existingRole);
        }

        await _userManager.AddToRoleAsync(user, role);

        return (user.Id, user.UserName ?? user.Id);
    }

    /// <inheritdoc />
    public async Task<LoginResult> PasswordSignInAsync(LoginRequest request, CancellationToken ct = default)
    {
        var result = await _signInManager.PasswordSignInAsync(
            request.UserName,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return new LoginResult
            {
                Succeeded = false,
                IsLockedOut = result.IsLockedOut
            };
        }

        // Resolve the user to update last login and return identity info
        var user = await _userManager.FindByNameAsync(request.UserName)
                   ?? await _userManager.FindByEmailAsync(request.UserName);

        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }

        return new LoginResult
        {
            Succeeded = true,
            IsLockedOut = false,
            UserId = user?.Id,
            UserName = user?.UserName ?? user?.Email ?? user?.Id
        };
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _signInManager.SignOutAsync();
    }

    /// <inheritdoc />
    public async Task<CreateUserResult> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            UserName = request.UserName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return new CreateUserResult
            {
                Succeeded = false,
                Errors = result.Errors.Select(e => e.Description).ToList()
            };
        }

        await _userManager.AddToRoleAsync(user, request.Role);

        return new CreateUserResult
        {
            Succeeded = true,
            UserId = user.Id,
            UserName = user.UserName ?? user.Email
        };
    }

    /// <inheritdoc />
    public async Task<(string UserId, string UserName)?> SetUserActiveStatusAsync(
        string userId, bool isActive, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        user.IsActive = isActive;
        await _userManager.UpdateAsync(user);

        return (user.Id, user.UserName ?? user.Id);
    }

    private static UserDto MapToDto(ApplicationUser user, IList<string> roles) => new()
    {
        Id = user.Id,
        UserName = user.UserName ?? string.Empty,
        Email = user.Email ?? string.Empty,
        DisplayName = user.DisplayName,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt,
        Roles = roles.ToList()
    };
}
