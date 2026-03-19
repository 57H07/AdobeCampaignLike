using Microsoft.AspNetCore.Identity;

namespace CampaignEngine.Infrastructure.Identity;

/// <summary>
/// Application role entity extending ASP.NET Core Identity's IdentityRole.
/// Three roles are defined: Designer, Operator, Admin.
/// </summary>
public class ApplicationRole : IdentityRole
{
    /// <summary>Human-readable description of what this role can do.</summary>
    public string? Description { get; set; }

    public ApplicationRole() : base() { }

    public ApplicationRole(string roleName) : base(roleName) { }

    public ApplicationRole(string roleName, string description) : base(roleName)
    {
        Description = description;
    }
}
