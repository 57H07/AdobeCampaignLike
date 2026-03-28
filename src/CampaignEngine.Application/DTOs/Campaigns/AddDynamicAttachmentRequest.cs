using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Request to register a dynamic attachment configuration for a campaign.
///
/// US-028 TASK-028-05: POST /api/campaigns/{id}/attachments (dynamic).
///
/// Dynamic attachments reference a data source field holding a per-recipient file path.
/// The file path is resolved at send time; no file is uploaded at registration.
/// </summary>
public class AddDynamicAttachmentRequest
{
    /// <summary>
    /// Data source field name whose value is the per-recipient file path.
    /// Must match a field declared in the campaign's data source schema.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string DynamicFieldName { get; set; } = string.Empty;
}
