namespace CampaignEngine.Application.DTOs.Identity;

/// <summary>
/// Request DTO for user authentication via username/email and password.
/// </summary>
public record LoginRequest
{
    /// <summary>Username or email address.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>User password.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Whether to persist the authentication cookie beyond the session.</summary>
    public bool RememberMe { get; init; }
}
