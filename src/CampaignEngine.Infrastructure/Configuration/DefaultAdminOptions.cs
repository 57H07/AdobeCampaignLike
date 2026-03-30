namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed configuration for the default admin account seeded on first deployment.
/// Bound from the "DefaultAdmin" section in appsettings.json / environment variables.
/// </summary>
public class DefaultAdminOptions
{
    public const string SectionName = "DefaultAdmin";

    /// <summary>Login username for the default admin account.</summary>
    public string UserName { get; init; } = "admin";

    /// <summary>Email address for the default admin account.</summary>
    public string Email { get; init; } = "admin@campaignengine.local";

    /// <summary>Password for the default admin account. Should be changed on first login.</summary>
    public string Password { get; init; } = "Admin@1234!";

    /// <summary>Friendly display name shown in the UI.</summary>
    public string? DisplayName { get; init; } = "Default Administrator";
}
