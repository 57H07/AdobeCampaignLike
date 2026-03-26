using CampaignEngine.Application.DTOs.Identity;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Abstracts ASP.NET Core Identity operations (user management, sign-in, sign-out)
/// so that the Web layer does not depend on Infrastructure Identity types.
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// Returns all application users with their assigned roles.
    /// </summary>
    Task<IReadOnlyList<UserDto>> GetAllUsersAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a single user by ID, or null if not found.
    /// </summary>
    Task<UserDto?> GetUserByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Replaces all current roles of a user with the specified role.
    /// Returns the user ID and username of the affected user, or null if user not found.
    /// </summary>
    /// <param name="userId">Target user ID.</param>
    /// <param name="role">New role to assign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of (userId, userName) for audit logging, or null if user not found.</returns>
    Task<(string UserId, string UserName)?> AssignRoleAsync(string userId, string role, CancellationToken ct = default);

    /// <summary>
    /// Authenticates a user with username/email and password.
    /// On success, issues the authentication cookie and updates the last login timestamp.
    /// </summary>
    Task<LoginResult> PasswordSignInAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Signs the current user out (removes authentication cookie).
    /// </summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new user with a password and assigns the specified role.
    /// </summary>
    Task<CreateUserResult> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sets the active status of a user (activate or deactivate).
    /// Returns the user ID and username for audit logging, or null if user not found.
    /// </summary>
    /// <param name="userId">Target user ID.</param>
    /// <param name="isActive">True to activate, false to deactivate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of (userId, userName) for audit logging, or null if user not found.</returns>
    Task<(string UserId, string UserName)?> SetUserActiveStatusAsync(string userId, bool isActive, CancellationToken ct = default);
}
