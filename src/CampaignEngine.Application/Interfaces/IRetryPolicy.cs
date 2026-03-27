using CampaignEngine.Application.DTOs.Dispatch;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Defines the retry policy for transient dispatch failures.
///
/// Business rules (US-035):
///   BR-1: Max 3 retry attempts per send.
///   BR-2: Exponential backoff: 30s, 2min (120s), 10min (600s).
///   BR-3: Only transient failures are retried; permanent failures are not.
///   BR-4: Retry count is tracked in SendLog.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts allowed.
    /// Default: 3 (configurable via CampaignEngineOptions.BatchProcessing.MaxRetryAttempts).
    /// </summary>
    int MaxAttempts { get; }

    /// <summary>
    /// Returns the delay to wait before the next retry attempt.
    /// </summary>
    /// <param name="attemptNumber">The retry attempt number (1 = first retry, 2 = second, etc.).</param>
    /// <returns>Delay before the next attempt.</returns>
    TimeSpan GetDelay(int attemptNumber);

    /// <summary>
    /// Determines whether a dispatch result should be retried.
    /// Returns true only if the result is a transient failure and the maximum
    /// number of attempts has not been reached.
    /// </summary>
    /// <param name="result">The dispatch result to evaluate.</param>
    /// <param name="currentRetryCount">The number of retries already attempted.</param>
    bool ShouldRetry(DispatchResult result, int currentRetryCount);

    /// <summary>
    /// Executes a dispatch function with automatic retry on transient failures.
    /// Logs the retry count to the send log after each attempt.
    /// </summary>
    /// <param name="operation">
    /// The dispatch operation to execute. Receives the current retry count (0 = first attempt).
    /// Returns the dispatch result.
    /// </param>
    /// <param name="onRetry">
    /// Optional callback invoked before each retry attempt.
    /// Receives (result, retryAttemptNumber, delay).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final dispatch result after all retry attempts.</returns>
    Task<DispatchResult> ExecuteAsync(
        Func<int, CancellationToken, Task<DispatchResult>> operation,
        Func<DispatchResult, int, TimeSpan, Task>? onRetry = null,
        CancellationToken cancellationToken = default);
}
