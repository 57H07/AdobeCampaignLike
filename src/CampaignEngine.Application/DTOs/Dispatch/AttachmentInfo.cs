namespace CampaignEngine.Application.DTOs.Dispatch;

/// <summary>
/// Attachment data to be included with a dispatched message.
/// </summary>
public class AttachmentInfo
{
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
}
