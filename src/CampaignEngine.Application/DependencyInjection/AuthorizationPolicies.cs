namespace CampaignEngine.Application.DependencyInjection;

/// <summary>
/// Named authorization policy constants used across controllers and Razor Pages.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Requires the Admin role. Full system access.</summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>Requires the Designer role. Template management access.</summary>
    public const string RequireDesigner = "RequireDesigner";

    /// <summary>Requires the Operator role. Campaign management access.</summary>
    public const string RequireOperator = "RequireOperator";

    /// <summary>Requires Admin or Designer role. Template read access for Operators is separate.</summary>
    public const string RequireDesignerOrAdmin = "RequireDesignerOrAdmin";

    /// <summary>Requires Admin or Operator role. Campaign operations.</summary>
    public const string RequireOperatorOrAdmin = "RequireOperatorOrAdmin";

    /// <summary>Any authenticated user (all three roles).</summary>
    public const string RequireAuthenticated = "RequireAuthenticated";
}
