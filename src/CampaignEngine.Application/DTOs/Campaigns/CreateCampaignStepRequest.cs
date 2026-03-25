using CampaignEngine.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Describes a single step in a campaign creation request.
/// </summary>
public class CreateCampaignStepRequest
{
    /// <summary>
    /// Step position (1-based). Steps are ordered by this value.
    /// Must be unique within the campaign.
    /// Business rule: maximum 10 steps per campaign (StepOrder 1–10).
    /// </summary>
    [Required]
    [Range(1, 10)]
    public int StepOrder { get; set; }

    /// <summary>
    /// Channel type for this step.
    /// Must be a valid ChannelType value: Email=1, Letter=2, Sms=3.
    /// </summary>
    [Required]
    public ChannelType Channel { get; set; }

    /// <summary>
    /// ID of the published template to use for this step.
    /// Business rule: only Published templates can be selected.
    /// </summary>
    [Required]
    public Guid TemplateId { get; set; }

    /// <summary>
    /// Delay in days from the previous step (0 = immediate / same day as previous).
    /// Must be non-negative.
    /// </summary>
    [Range(0, 3650)]
    public int DelayDays { get; set; } = 0;

    /// <summary>
    /// Optional JSON-serialized step-specific filter expression.
    /// AND-combined with the campaign base filter when evaluated.
    /// </summary>
    public string? StepFilter { get; set; }
}
