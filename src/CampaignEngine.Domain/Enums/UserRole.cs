namespace CampaignEngine.Domain.Enums;

/// <summary>
/// Defines the application roles governing feature access.
/// </summary>
public static class UserRole
{
    /// <summary>
    /// Full template CRUD + preview access. No campaign access.
    /// </summary>
    public const string Designer = "Designer";

    /// <summary>
    /// Full campaign CRUD + monitoring. Read-only template access.
    /// </summary>
    public const string Operator = "Operator";

    /// <summary>
    /// Full access to all features, user management, and configuration.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// All defined roles for seeding and enumeration.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[] { Admin, Designer, Operator };

    /// <summary>
    /// Default role assigned to newly created users.
    /// </summary>
    public const string Default = Operator;
}
