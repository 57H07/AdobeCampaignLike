using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Request body for POST /api/campaigns — creates a new campaign in Draft status.
/// </summary>
public class CreateCampaignRequest
{
    /// <summary>
    /// Unique campaign name (required, max 300 characters).
    /// Business rule: campaign name must be unique.
    /// </summary>
    [Required]
    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ID of the data source for recipient targeting.
    /// </summary>
    public Guid? DataSourceId { get; set; }

    /// <summary>
    /// JSON-serialized filter expression AST for recipient targeting.
    /// Null means no filter — all records in the data source are targeted.
    /// </summary>
    public string? FilterExpression { get; set; }

    /// <summary>
    /// JSON-serialized dictionary of operator-provided free field values.
    /// Keys correspond to freeField placeholder keys declared in the template manifest.
    /// Business rule: all freeField placeholders in the selected template must be provided.
    /// </summary>
    public string? FreeFieldValues { get; set; }

    /// <summary>
    /// UTC date/time when the campaign should start.
    /// Null = campaign will be manually triggered.
    /// Business rule: if provided, must be at least 5 minutes in the future.
    /// </summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>
    /// Ordered list of campaign steps (at least one, maximum 10 steps).
    /// Business rule: a campaign may have at most 10 steps.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one step is required.")]
    [MaxLength(10, ErrorMessage = "A campaign may have at most 10 steps.")]
    public List<CreateCampaignStepRequest> Steps { get; set; } = new();

    // ----------------------------------------------------------------
    // CC/BCC configuration (US-029)
    // ----------------------------------------------------------------

    /// <summary>
    /// Static CC addresses: comma-separated list of email addresses.
    /// All recipients of each send will receive a CC to these addresses.
    /// Business rule: comma-separated, max 10 CC recipients per send (enforced at dispatch time).
    /// </summary>
    [MaxLength(2000)]
    public string? StaticCcAddresses { get; set; }

    /// <summary>
    /// Data source field name containing the dynamic CC address(es) per recipient.
    /// The field value may contain a single email or a semicolon-separated list.
    /// Business rule: field value is extracted per-recipient from the data source row.
    /// </summary>
    [MaxLength(200)]
    public string? DynamicCcField { get; set; }

    /// <summary>
    /// Static BCC addresses: comma-separated list of email addresses.
    /// Hidden copies — BCC recipients are not visible to the To or CC recipients.
    /// Business rule: comma-separated, validated, deduplicated.
    /// </summary>
    [MaxLength(2000)]
    public string? StaticBccAddresses { get; set; }
}
