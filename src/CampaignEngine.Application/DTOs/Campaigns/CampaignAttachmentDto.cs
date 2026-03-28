namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// DTO representing a campaign attachment returned from the API.
///
/// US-028: Static and dynamic attachment management.
/// </summary>
public class CampaignAttachmentDto
{
    /// <summary>Unique attachment identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Campaign this attachment belongs to.</summary>
    public Guid CampaignId { get; init; }

    /// <summary>Display file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Absolute path on the file share (empty for dynamic attachments).</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>File size in bytes (0 for dynamic attachments).</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>MIME content type (e.g. "application/pdf").</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    /// True if this is a dynamic attachment (resolved per recipient at send time).
    /// False for static attachments (same file for all recipients).
    /// </summary>
    public bool IsDynamic { get; init; }

    /// <summary>
    /// For dynamic attachments: the data source field name containing the file path.
    /// Null for static attachments.
    /// </summary>
    public string? DynamicFieldName { get; init; }

    /// <summary>UTC timestamp when the attachment was created.</summary>
    public DateTime CreatedAt { get; init; }
}
