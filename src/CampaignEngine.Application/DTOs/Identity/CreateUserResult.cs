namespace CampaignEngine.Application.DTOs.Identity;

/// <summary>
/// Result DTO for a user creation attempt.
/// </summary>
public record CreateUserResult
{
    /// <summary>Whether the user was created successfully.</summary>
    public bool Succeeded { get; init; }

    /// <summary>User ID of the newly created user (null if creation failed).</summary>
    public string? UserId { get; init; }

    /// <summary>Username of the newly created user (null if creation failed).</summary>
    public string? UserName { get; init; }

    /// <summary>Validation/identity errors if creation failed.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
