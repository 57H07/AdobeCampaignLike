namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Aggregate dashboard metrics for all active campaigns.
/// Returned by GET /api/campaigns/dashboard.
/// </summary>
public class CampaignDashboardDto
{
    /// <summary>UTC timestamp when these metrics were computed.</summary>
    public DateTime ComputedAtUtc { get; init; }

    /// <summary>Total number of active campaigns (Running or StepInProgress).</summary>
    public int ActiveCampaignCount { get; init; }

    /// <summary>Per-campaign metric cards, ordered by start time descending.</summary>
    public IReadOnlyList<CampaignProgressDto> Campaigns { get; init; } = Array.Empty<CampaignProgressDto>();
}

/// <summary>
/// Per-campaign progress metrics for the dashboard.
/// </summary>
public class CampaignProgressDto
{
    /// <summary>Campaign identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Campaign display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Current lifecycle status string.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Username of the operator who created the campaign.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Total number of recipients targeted.</summary>
    public int TotalRecipients { get; init; }

    /// <summary>Number of recipients processed so far.</summary>
    public int ProcessedCount { get; init; }

    /// <summary>Number of successful sends.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of failed sends.</summary>
    public int FailureCount { get; init; }

    /// <summary>Progress percentage (0-100). Zero when TotalRecipients is 0.</summary>
    public int ProgressPercent => TotalRecipients > 0
        ? (int)Math.Round(ProcessedCount * 100.0 / TotalRecipients)
        : 0;

    /// <summary>Failure rate percentage (0-100). Zero when TotalRecipients is 0.</summary>
    public double FailureRatePercent => TotalRecipients > 0
        ? Math.Round(FailureCount * 100.0 / TotalRecipients, 2)
        : 0;

    /// <summary>
    /// Estimated completion time in UTC.
    /// Null when no recipients have been processed yet or campaign is complete.
    /// Computed from the current send rate since campaign start.
    /// </summary>
    public DateTime? EstimatedCompletionUtc { get; init; }

    /// <summary>UTC timestamp when the campaign started executing.</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>UTC timestamp when the campaign was scheduled.</summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>Ordered step timeline entries for multi-step visualization.</summary>
    public IReadOnlyList<CampaignStepProgressDto> Steps { get; init; } = Array.Empty<CampaignStepProgressDto>();
}

/// <summary>
/// Per-step progress entry for multi-step campaign timeline visualization.
/// </summary>
public class CampaignStepProgressDto
{
    /// <summary>Step identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Step position (1-based).</summary>
    public int StepOrder { get; init; }

    /// <summary>Channel type string: Email, Sms, Letter.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>Template name for this step.</summary>
    public string? TemplateName { get; init; }

    /// <summary>Delay in days from the previous step (0 = immediate).</summary>
    public int DelayDays { get; init; }

    /// <summary>UTC date/time when this step was scheduled to run.</summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>UTC date/time when this step was executed.</summary>
    public DateTime? ExecutedAt { get; init; }

    /// <summary>
    /// Step status: Pending, Active, Completed, Waiting.
    /// Derived from ExecutedAt / ScheduledAt relative to now.
    /// </summary>
    public string StepStatus { get; init; } = "Pending";
}

/// <summary>
/// Query filter for the dashboard endpoint.
/// </summary>
public class DashboardFilter
{
    /// <summary>
    /// Optional status filter. When null, defaults to Running and StepInProgress.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>Start of date range filter (UTC). Null = no lower bound.</summary>
    public DateTime? StartedFrom { get; set; }

    /// <summary>End of date range filter (UTC). Null = no upper bound.</summary>
    public DateTime? StartedTo { get; set; }

    /// <summary>Filter by operator username. Null = all operators.</summary>
    public string? CreatedBy { get; set; }
}
