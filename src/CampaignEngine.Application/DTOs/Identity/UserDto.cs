namespace CampaignEngine.Application.DTOs.Identity;

/// <summary>
/// DTO representing an application user with their assigned roles.
/// </summary>
public record UserDto
{
    /// <summary>Unique identifier (Identity user ID).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Login username.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Friendly display name shown in the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Whether the account is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>UTC timestamp when the account was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC timestamp of the last successful login.</summary>
    public DateTime? LastLoginAt { get; init; }

    /// <summary>Roles assigned to the user.</summary>
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}
