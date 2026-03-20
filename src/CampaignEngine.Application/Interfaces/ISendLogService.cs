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

    /// <summary>
    /// Updates the delivery status of a send log entry based on an inbound provider callback.
    /// Looks up the send log by the provider-assigned external message ID.
    /// If no matching entry is found, the call is a no-op (provider may have retried).
    ///
    /// TASK-020-05: Delivery status callback tracking.
    /// </summary>
    /// <param name="externalMessageId">Provider message ID (e.g. Twilio MessageSid).</param>
    /// <param name="delivered">True if the provider confirmed delivery; false if failed.</param>
    /// <param name="errorDetail">Optional error description when <paramref name="delivered"/> is false.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateDeliveryStatusAsync(
        string externalMessageId,
        bool delivered,
        string? errorDetail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the provider-assigned external message ID on a send log entry.
    /// Called after a successful dispatch to enable delivery receipt correlation.
    /// TASK-020-05.
    /// </summary>
    Task SetExternalMessageIdAsync(
        Guid sendLogId,
        string externalMessageId,
        CancellationToken cancellationToken = default);
}
