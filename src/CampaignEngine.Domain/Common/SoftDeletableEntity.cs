namespace CampaignEngine.Domain.Common;

/// <summary>
/// Base entity with soft delete support for critical domain objects.
/// </summary>
public abstract class SoftDeletableEntity : AuditableEntity
{
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
