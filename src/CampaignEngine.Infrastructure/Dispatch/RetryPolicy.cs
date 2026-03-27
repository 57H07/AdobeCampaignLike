using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Implements the retry policy with exponential backoff for transient dispatch failures.
///
/// Business rules (US-035):
///   BR-1: Max 3 retry attempts per send (configurable via CampaignEngineOptions).
///   BR-2: Backoff delays: 30s, 2min (120s), 10min (600s) — configurable.
///   BR-3: Only transient failures are retried (IsTransientFailure = true).
///   BR-4: Permanent failures (invalid email, template error) are never retried.
///
/// Configuration: CampaignEngine:BatchProcessing:MaxRetryAttempts (default 3)
///                CampaignEngine:BatchProcessing:RetryDelaysSeconds (default [30, 120, 600])
/// </summary>
public class RetryPolicy : IRetryPolicy
{
    private readonly int _maxAttempts;
    private readonly int[] _delaysSeconds;

    public RetryPolicy(IOptions<CampaignEngineOptions> options)
    {
        var batchOptions = options.Value.BatchProcessing;
        _maxAttempts = batchOptions.MaxRetryAttempts;
        _delaysSeconds = batchOptions.RetryDelaysSeconds;
    }

    /// <inheritdoc />
    public int MaxAttempts => _maxAttempts;

    /// <inheritdoc />
    public TimeSpan GetDelay(int attemptNumber)
    {
        // attemptNumber is 1-based: 1 = first retry delay, 2 = second, etc.
        // Map to 0-based index into the delays array.
        var index = Math.Max(0, attemptNumber - 1);
        index = Math.Min(index, _delaysSeconds.Length - 1);
        return TimeSpan.FromSeconds(_delaysSeconds[index]);
    }

    /// <inheritdoc />
    public bool ShouldRetry(DispatchResult result, int currentRetryCount)
    {
        // Only retry transient failures
        if (result.Success)
            return false;

        if (!result.IsTransientFailure)
            return false;

        // Check we haven't exceeded max attempts
        return currentRetryCount < _maxAttempts;
    }

    /// <inheritdoc />
    public async Task<DispatchResult> ExecuteAsync(
        Func<int, CancellationToken, Task<DispatchResult>> operation,
        Func<DispatchResult, int, TimeSpan, Task>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        DispatchResult result;

        do
        {
            result = await operation(retryCount, cancellationToken);

            if (!ShouldRetry(result, retryCount))
                break;

            retryCount++;
            var delay = GetDelay(retryCount);

            if (onRetry is not null)
            {
                await onRetry(result, retryCount, delay);
            }

            await Task.Delay(delay, cancellationToken);
        }
        while (retryCount <= _maxAttempts);

        return result;
    }
}
