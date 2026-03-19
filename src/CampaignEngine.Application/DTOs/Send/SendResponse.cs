using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.Send;

/// <summary>
/// Response returned by the POST /api/send endpoint after a single send attempt.
/// </summary>
public class SendResponse
{
    /// <summary>
    /// Unique tracking ID for this send operation.
    /// Can be used to look up the corresponding SendLog entry.
    /// </summary>
    public Guid TrackingId { get; set; }

    /// <summary>
    /// Whether the message was dispatched successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The final send status.
    /// </summary>
    public SendStatus Status { get; set; }

    /// <summary>
    /// The channel through which the message was sent.
    /// </summary>
    public ChannelType Channel { get; set; }

    /// <summary>
    /// UTC timestamp when the message was dispatched.
    /// </summary>
    public DateTime SentAt { get; set; }

    /// <summary>
    /// Dispatcher-provided message ID (e.g. SMTP message-id, SMS provider ID).
    /// Null if unavailable or on failure.
    /// </summary>
    public string? MessageId { get; set; }

    /// <summary>
    /// Human-readable error description when Success is false.
    /// </summary>
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    public static SendResponse Ok(
        Guid trackingId,
        ChannelType channel,
        DateTime sentAt,
        string? messageId = null) => new()
    {
        TrackingId = trackingId,
        Success = true,
        Status = SendStatus.Sent,
        Channel = channel,
        SentAt = sentAt,
        MessageId = messageId
    };

    /// <summary>
    /// Creates a failure response.
    /// </summary>
    public static SendResponse Fail(
        Guid trackingId,
        ChannelType channel,
        string errorDetail) => new()
    {
        TrackingId = trackingId,
        Success = false,
        Status = SendStatus.Failed,
        Channel = channel,
        SentAt = DateTime.UtcNow,
        ErrorDetail = errorDetail
    };
}
