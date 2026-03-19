using CampaignEngine.Domain.Common;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents a file attachment associated with a campaign.
/// Static attachments are the same for all recipients.
/// Dynamic attachments reference a data source field for per-recipient files.
/// </summary>
public class CampaignAttachment : AuditableEntity
{
    public Guid CampaignId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public bool IsDynamic { get; set; } = false;

    /// <summary>
    /// For dynamic attachments: the data source field name containing the file path.
    /// </summary>
    public string? DynamicFieldName { get; set; }

    // Navigation property
    public Campaign? Campaign { get; set; }
}
