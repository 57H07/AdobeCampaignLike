using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents a campaign that sends messages to a targeted population.
/// Campaigns support soft delete and multi-step sequences.
/// </summary>
public class Campaign : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public Guid? DataSourceId { get; set; }

    /// <summary>
    /// JSON-serialized filter expression AST for recipient targeting.
    /// </summary>
    public string? FilterExpression { get; set; }

    /// <summary>
    /// JSON-serialized dictionary of operator-provided free field values.
    /// </summary>
    public string? FreeFieldValues { get; set; }

    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Progress tracking
    public int TotalRecipients { get; set; } = 0;
    public int ProcessedCount { get; set; } = 0;
    public int SuccessCount { get; set; } = 0;
    public int FailureCount { get; set; } = 0;

    // CC configuration
    public string? StaticCcAddresses { get; set; }
    public string? DynamicCcField { get; set; }
    public string? StaticBccAddresses { get; set; }

    public string? CreatedBy { get; set; }

    // Navigation properties
    public DataSource? DataSource { get; set; }
    public ICollection<CampaignStep> Steps { get; set; } = new List<CampaignStep>();
    public ICollection<CampaignAttachment> Attachments { get; set; } = new List<CampaignAttachment>();
    public ICollection<SendLog> SendLogs { get; set; } = new List<SendLog>();
}
