using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents a campaign that sends messages to a targeted population.
/// Campaigns support soft delete and multi-step sequences.
/// </summary>
public class Campaign : SoftDeletableEntity
{
    private static readonly TimeSpan MinScheduleAhead = TimeSpan.FromMinutes(5);
    private const int MaxSteps = 10;

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

    // ----------------------------------------------------------------
    // Domain behaviour
    // ----------------------------------------------------------------

    /// <summary>
    /// Adds a step to the campaign, enforcing the maximum of 10 steps.
    /// </summary>
    /// <exception cref="DomainException">Thrown when adding the step would exceed the 10-step limit.</exception>
    public void AddStep(CampaignStep step)
    {
        if (Steps.Count >= MaxSteps)
            throw new DomainException(
                $"A campaign may have at most {MaxSteps} steps. Cannot add step #{Steps.Count + 1}.");

        Steps.Add(step);
    }

    /// <summary>
    /// Transitions the campaign from Draft to Scheduled, enforcing all scheduling invariants.
    /// </summary>
    /// <exception cref="DomainException">
    /// Thrown when the campaign is not in Draft status, has no ScheduledAt value set,
    /// or is scheduled less than 5 minutes in the future.
    /// </exception>
    public void Schedule()
    {
        if (Status != CampaignStatus.Draft)
            throw new DomainException(
                $"Campaign must be in Draft status to schedule. Current: {Status}.");

        if (!ScheduledAt.HasValue)
            throw new DomainException("Campaign ScheduledAt must be set before scheduling.");

        var minSchedule = DateTime.UtcNow.Add(MinScheduleAhead);
        if (ScheduledAt.Value < minSchedule)
            throw new DomainException(
                $"Scheduled date must be at least 5 minutes in the future (minimum: {minSchedule:yyyy-MM-dd HH:mm} UTC).");

        Status = CampaignStatus.Scheduled;
    }
}
