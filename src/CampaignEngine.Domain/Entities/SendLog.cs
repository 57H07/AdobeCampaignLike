using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Records every send attempt — the source of truth for all dispatch activity.
/// All sends are logged before dispatch and updated after result.
/// </summary>
public class SendLog : AuditableEntity
{
    public Guid CampaignId { get; set; }
    public Guid? CampaignStepId { get; set; }
    public ChannelType Channel { get; set; }
    public SendStatus Status { get; set; } = SendStatus.Pending;

    public string RecipientAddress { get; set; } = string.Empty;
    public string? RecipientId { get; set; }

    public DateTime? SentAt { get; set; }
    public int RetryCount { get; set; } = 0;
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// Correlation ID linking this send to an external request or campaign chunk.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Provider-assigned message identifier returned after a successful dispatch.
    /// Used to correlate inbound delivery status callbacks with this log entry.
    /// E.g. Twilio MessageSid, or the "id" field from a generic SMS provider response.
    /// TASK-020-05: delivery status tracking.
    /// </summary>
    public string? ExternalMessageId { get; set; }

    // Navigation properties
    public Campaign? Campaign { get; set; }
}
