namespace CampaignEngine.Application.DTOs.Identity;

/// <summary>
/// Request DTO for creating a new application user.
/// </summary>
public record CreateUserRequest
{
    /// <summary>Login username.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Friendly display name shown in the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Password for the new account.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Role to assign (Admin, Designer, or Operator).</summary>
    public string Role { get; init; } = string.Empty;
}
