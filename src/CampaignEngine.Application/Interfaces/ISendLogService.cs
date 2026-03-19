using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service responsible for recording send attempts in SEND_LOG.
/// All sends must be logged before dispatch (Pending) and updated after result (Sent/Failed/Retrying).
/// SEND_LOG is the source of truth for all dispatch activity.
/// </summary>
public interface ISendLogService
{
    /// <summary>
    /// Creates a new send log entry with Pending status before dispatch.
    /// Returns the ID of the created log entry for subsequent status updates.
    /// </summary>
    Task<Guid> LogPendingAsync(
        Guid campaignId,
        Guid? campaignStepId,
        ChannelType channel,
        string recipientAddress,
        string? recipientId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a send log entry to Sent status after successful dispatch.
    /// </summary>
    Task LogSentAsync(
        Guid sendLogId,
        DateTime sentAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a send log entry to Failed status after permanent dispatch failure.
    /// </summary>
    Task LogFailedAsync(
        Guid sendLogId,
        string errorDetail,
        int retryCount = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a send log entry to Retrying status for transient failures.
    /// Increments retry count.
    /// </summary>
    Task LogRetryingAsync(
        Guid sendLogId,
        string errorDetail,
        int retryCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries send logs with optional filtering.
    /// Supports filtering by campaign, recipient address, status, and date range.
    /// </summary>
    Task<IReadOnlyList<SendLog>> QueryAsync(
        Guid? campaignId = null,
        string? recipientAddress = null,
        SendStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of send logs matching the given filters.
    /// </summary>
    Task<int> CountAsync(
        Guid? campaignId = null,
        string? recipientAddress = null,
        SendStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single send log by ID.
    /// </summary>
    Task<SendLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
