using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.Dispatch;

/// <summary>
/// Request model for dispatching a single message through a channel dispatcher.
/// Extended in US-019 to support email-specific fields: subject, BCC, Reply-To.
/// </summary>
public class DispatchRequest
{
    public ChannelType Channel { get; set; }

    /// <summary>The rendered HTML (email) or text (SMS) content to send.</summary>
    public string Content { get; set; } = string.Empty;

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
