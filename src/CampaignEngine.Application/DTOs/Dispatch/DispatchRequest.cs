using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.Dispatch;

/// <summary>
/// Request model for dispatching a single message through a channel dispatcher.
/// </summary>
public class DispatchRequest
{
    public ChannelType Channel { get; set; }
    public string Content { get; set; } = string.Empty;
    public RecipientInfo Recipient { get; set; } = new();
    public List<string> CcAddresses { get; set; } = [];
    public List<AttachmentInfo> Attachments { get; set; } = [];
    public Guid? CampaignId { get; set; }
    public Guid? CampaignStepId { get; set; }
}
