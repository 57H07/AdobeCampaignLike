using CampaignEngine.Application.DTOs.Dispatch;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Orchestrates dispatch with full SEND_LOG lifecycle logging.
/// Ensures every send attempt is recorded before and after dispatch.
/// </summary>
public interface ILoggingDispatchOrchestrator
{
    /// <summary>
    /// Sends a message via the appropriate channel dispatcher while logging the attempt to SEND_LOG.
    /// Logs Pending before dispatch, then Sent / Failed / Retrying based on the result.
    /// </summary>
    /// <param name="request">The dispatch request containing channel, content, and recipient info.</param>
    /// <param name="correlationId">Optional correlation ID to link this send to a broader operation.</param>
    /// <param name="currentRetryCount">Current retry attempt number (used when retrying).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SEND_LOG entry ID and the dispatch result.</returns>
    Task<(Guid SendLogId, DispatchResult Result)> SendWithLoggingAsync(
        DispatchRequest request,
        string? correlationId = null,
        int currentRetryCount = 0,
        CancellationToken cancellationToken = default);
}
