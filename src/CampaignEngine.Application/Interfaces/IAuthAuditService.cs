namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Records authentication and authorization events to the audit log.
/// All security-relevant events must be captured for compliance.
/// </summary>
public interface IAuthAuditService
{
    /// <summary>
    /// Logs a successful login event.
    /// </summary>
    Task LogLoginAsync(string userId, string userName, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Logs a failed login attempt.
    /// </summary>
    Task LogLoginFailedAsync(string userName, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Logs a logout event.
    /// </summary>
    Task LogLogoutAsync(string userId, string userName, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Logs a role assignment event (performed by an admin user).
    /// </summary>
    Task LogRoleAssignedAsync(string adminUserId, string adminUserName, string targetUserId, string targetUserName, string role, CancellationToken ct = default);

    /// <summary>
    /// Logs a role removal event (performed by an admin user).
    /// </summary>
    Task LogRoleRemovedAsync(string adminUserId, string adminUserName, string targetUserId, string targetUserName, string role, CancellationToken ct = default);

    /// <summary>
    /// Logs a user account creation.
    /// </summary>
    Task LogUserCreatedAsync(string adminUserId, string adminUserName, string newUserId, string newUserName, CancellationToken ct = default);

    /// <summary>
    /// Logs a custom security event.
    /// </summary>
    Task LogEventAsync(string eventType, string? userId, string? userName, string? details, string? ipAddress, bool succeeded, CancellationToken ct = default);
}
