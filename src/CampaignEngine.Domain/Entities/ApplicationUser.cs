namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents an application user with extended profile information.
/// This class extends ASP.NET Core Identity's IdentityUser.
/// The actual Identity base class is applied in the Infrastructure layer
/// via the <see cref="CampaignEngine.Infrastructure.Identity.ApplicationUser"/> wrapper.
/// This domain interface captures the required business fields.
/// </summary>
public interface IApplicationUser
{
    string Id { get; }
    string UserName { get; }
    string Email { get; }
    string? DisplayName { get; }
    bool IsActive { get; }
    DateTime CreatedAt { get; }
    DateTime? LastLoginAt { get; }
}
