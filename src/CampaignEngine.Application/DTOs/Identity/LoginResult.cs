namespace CampaignEngine.Application.DTOs.Identity;

/// <summary>
/// Result DTO for a login attempt, abstracting Identity's SignInResult.
/// </summary>
public record LoginResult
{
    /// <summary>Whether the login was successful.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Whether the account is locked out due to too many failed attempts.</summary>
    public bool IsLockedOut { get; init; }

    /// <summary>User ID of the authenticated user (null if login failed).</summary>
    public string? UserId { get; init; }

    /// <summary>Username of the authenticated user (null if login failed).</summary>
    public string? UserName { get; init; }
}
