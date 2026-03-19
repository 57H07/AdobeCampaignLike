namespace CampaignEngine.Domain.Common;

/// <summary>
/// Base entity with audit fields for all persisted domain objects.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
