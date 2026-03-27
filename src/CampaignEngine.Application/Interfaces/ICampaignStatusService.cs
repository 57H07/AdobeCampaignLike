using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Validates and enforces allowed campaign status transitions.
/// The lifecycle enforces strict direction: statuses cannot move backwards.
///
/// Allowed transitions:
///   Draft          → Scheduled
///   Scheduled      → Running
///   Running        → StepInProgress
///   StepInProgress → WaitingNext | Completed | PartialFailure | ManualReview
///   WaitingNext    → StepInProgress | Completed | PartialFailure | ManualReview
/// </summary>
public interface ICampaignStatusService
{
    /// <summary>
    /// Returns true if the given status transition is valid.
    /// </summary>
    bool IsTransitionAllowed(CampaignStatus from, CampaignStatus to);

    /// <summary>
    /// Returns all valid next statuses from the given current status.
    /// </summary>
    IReadOnlyList<CampaignStatus> GetAllowedTransitions(CampaignStatus from);

    /// <summary>
    /// Returns true if the given status is a terminal state
    /// (i.e. no further automatic transitions are possible).
    /// Terminal statuses: Completed, PartialFailure, ManualReview.
    /// </summary>
    bool IsTerminal(CampaignStatus status);

    /// <summary>
    /// Returns true if the given status is an active execution state
    /// (i.e. the campaign is currently being processed).
    /// Active statuses: Running, StepInProgress, WaitingNext.
    /// </summary>
    bool IsActive(CampaignStatus status);

    /// <summary>
    /// Records a status transition event with a UTC timestamp in the campaign's status history.
    /// Persists to the CampaignStatusHistory table.
    /// </summary>
    /// <param name="campaignId">Campaign being transitioned.</param>
    /// <param name="from">Previous status.</param>
    /// <param name="to">New status.</param>
    /// <param name="reason">Optional free-text reason for the transition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogTransitionAsync(
        Guid campaignId,
        CampaignStatus from,
        CampaignStatus to,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full status transition history for a campaign, ordered ascending by timestamp.
    /// </summary>
    Task<IReadOnlyList<CampaignStatusTransitionDto>> GetHistoryAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single status transition event in a campaign's history.
/// </summary>
public record CampaignStatusTransitionDto(
    Guid Id,
    Guid CampaignId,
    string FromStatus,
    string ToStatus,
    string? Reason,
    DateTime OccurredAt);
