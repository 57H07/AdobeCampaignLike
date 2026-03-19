namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Audit trail record for authentication and authorization events.
/// Provides a tamper-evident log of security events for compliance.
/// </summary>
public class AuthAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The type of security event (e.g., Login, Logout, LoginFailed, RoleAssigned).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Identity of the user involved (user ID or username).</summary>
    public string? UserId { get; set; }

    /// <summary>Username for display in audit reports.</summary>
    public string? UserName { get; set; }

    /// <summary>Additional context (e.g., target user for role assignment).</summary>
    public string? Details { get; set; }

    /// <summary>Client IP address.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Whether the event represents a successful operation.</summary>
    public bool Succeeded { get; set; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Well-known authentication event type constants.
/// </summary>
public static class AuthEventType
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string PasswordChanged = "PasswordChanged";
    public const string RoleAssigned = "RoleAssigned";
    public const string RoleRemoved = "RoleRemoved";
    public const string UserCreated = "UserCreated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserReactivated = "UserReactivated";
}
