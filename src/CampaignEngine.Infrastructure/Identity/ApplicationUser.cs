using Microsoft.AspNetCore.Identity;

namespace CampaignEngine.Infrastructure.Identity;

/// <summary>
/// Application user entity extending ASP.NET Core Identity's IdentityUser.
/// Adds campaign engine-specific profile fields.
/// Default role for new users: Operator (per business rule 4).
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Friendly display name shown in the UI.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Whether the account is active. Inactive accounts cannot log in.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when the account was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last successful login.</summary>
    public DateTime? LastLoginAt { get; set; }
}
