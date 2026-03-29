using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.Dispatch;

/// <summary>
/// Request model for dispatching a single message through a channel dispatcher.
/// Extended in US-019 to support email-specific fields: subject, BCC, Reply-To.
/// Extended in US-022 (F-403b) to support binary content for the Letter channel.
///
/// Mutual exclusivity rule (Business Rule 1):
///   - Email and SMS channels set <see cref="Content"/> (rendered HTML or plain text) and leave
///     <see cref="BinaryContent"/> null.
///   - The Letter channel sets <see cref="BinaryContent"/> (pre-rendered DOCX bytes) and leaves
///     <see cref="Content"/> null.
///   Only one of <see cref="Content"/> / <see cref="BinaryContent"/> should be non-null per request.
///   Dispatchers determine which is set by null-checking each property.
/// </summary>
public class DispatchRequest
{
    public ChannelType Channel { get; set; }

    /// <summary>
    /// The rendered HTML (email) or plain text (SMS) content to send.
    /// Set for Email and SMS channels; null for Letter channel.
    /// Mutual exclusivity: when <see cref="BinaryContent"/> is set, this should be null.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Pre-rendered binary content (e.g. DOCX bytes) for the Letter channel. F-403b.
    /// Set for the Letter channel; null for Email and SMS channels.
    /// Mutual exclusivity: when <see cref="Content"/> is set, this should be null.
    /// Dispatchers check this property first; if non-null, they use it directly
    /// without any additional rendering step.
    /// </summary>
    public byte[]? BinaryContent { get; set; }

    /// <summary>The primary recipient of the message.</summary>
    public RecipientInfo Recipient { get; set; } = new();

    /// <summary>CC recipient email addresses (email channel only).</summary>
    public List<string> CcAddresses { get; set; } = [];

    /// <summary>BCC recipient email addresses (email channel only). TASK-019-04.</summary>
    public List<string> BccAddresses { get; set; } = [];

    /// <summary>
    /// Optional Reply-To address override. If null, uses the SMTP default from SmtpOptions.
    /// Business rule 2: Reply-To is optional per campaign.
    /// </summary>
    public string? ReplyToAddress { get; set; }

    /// <summary>Email subject line (email channel only).</summary>
    public string? Subject { get; set; }

    /// <summary>File attachments to include. TASK-019-03.</summary>
    public List<AttachmentInfo> Attachments { get; set; } = [];

    public Guid? CampaignId { get; set; }
    public Guid? CampaignStepId { get; set; }
}
