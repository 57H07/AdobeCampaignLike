namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Real-time status snapshot for a campaign, including progress counters
/// and the full status transition history.
/// </summary>
public class CampaignStatusDto
{
    /// <summary>Campaign identifier.</summary>
    public Guid CampaignId { get; init; }

    /// <summary>Campaign display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Current lifecycle status (string representation).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Total number of recipients targeted.</summary>
    public int TotalRecipients { get; init; }

    /// <summary>Number of recipients processed so far.</summary>
    public int ProcessedCount { get; init; }

    /// <summary>Number of successful sends.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of failed sends.</summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Progress percentage (0-100). Zero when TotalRecipients is 0.
    /// </summary>
    public int ProgressPercent => TotalRecipients > 0
        ? (int)Math.Round(ProcessedCount * 100.0 / TotalRecipients)
        : 0;

    /// <summary>
    /// Failure rate percentage (0-100). Zero when TotalRecipients is 0.
    /// </summary>
    public double FailureRatePercent => TotalRecipients > 0
        ? Math.Round(FailureCount * 100.0 / TotalRecipients, 2)
        : 0;

    /// <summary>UTC timestamp when the campaign started executing. Null if not yet started.</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>UTC timestamp when the campaign completed. Null if not yet completed.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Ordered list of status transitions with timestamps.
    /// Empty if no transitions have been recorded yet.
    /// </summary>
    public IReadOnlyList<CampaignStatusTransitionEntry> History { get; init; } = Array.Empty<CampaignStatusTransitionEntry>();
}

/// <summary>
/// A single status transition event in a campaign's history.
/// </summary>
public class CampaignStatusTransitionEntry
{
    /// <summary>Previous status.</summary>
    public string FromStatus { get; init; } = string.Empty;

    /// <summary>New status.</summary>
    public string ToStatus { get; init; } = string.Empty;

    /// <summary>Optional reason for the transition.</summary>
    public string? Reason { get; init; }

    /// <summary>UTC timestamp when the transition occurred.</summary>
    public DateTime OccurredAt { get; init; }
}
