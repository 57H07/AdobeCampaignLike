namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Provides access to the currently authenticated user's context.
/// Abstracted to support multiple authentication strategies.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the unique identifier of the current user.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets the name of the current user.
    /// </summary>
    string? UserName { get; }

    /// <summary>
    /// Gets the roles assigned to the current user.
    /// </summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Indicates whether a user is currently authenticated.
    /// </summary>
    bool IsAuthenticated { get; }
}
