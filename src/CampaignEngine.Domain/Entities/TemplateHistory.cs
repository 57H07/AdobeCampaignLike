using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Immutable snapshot of a template version for audit and rollback purposes.
/// Version history is never deleted.
/// </summary>
public class TemplateHistory : AuditableEntity
{
    public Guid TemplateId { get; set; }
    public int Version { get; set; }

    /// <summary>
    /// Relative path from storage root to the template body file at this version.
    /// </summary>
    public string BodyPath { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex checksum of the template body file (64 hex characters), nullable.
    /// </summary>
    public string? BodyChecksum { get; set; }

    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string? ChangedBy { get; set; }

    // Navigation property
    public Template? Template { get; set; }
}
