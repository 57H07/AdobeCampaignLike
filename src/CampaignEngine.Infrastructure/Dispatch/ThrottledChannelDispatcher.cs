using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Decorator around <see cref="IChannelDispatcher"/> that applies rate limiting
/// before delegating to the inner dispatcher.
///
/// US-022 TASK-022-03: Integrate throttling in dispatchers.
/// US-022 TASK-022-04: Queue backpressure handling.
///
/// Throttling strategy:
///   1. Acquire a token from the channel's rate limiter before each send.
///   2. If the bucket has tokens available: acquire immediately and dispatch.
///   3. If the bucket is exhausted: apply backpressure by waiting (WaitAsync).
///   4. If the wait times out: return a transient DispatchResult so the retry
///      policy (US-035) will retry with exponential backoff (BR-3).
///
/// Metrics are recorded for observability (TASK-022-05):
///   - Token acquired (immediate or after wait)
///   - Throttle wait with duration
///   - Rate limit exceeded (timeout)
/// </summary>
public sealed class ThrottledChannelDispatcher : IChannelDispatcher
{
    private readonly IChannelDispatcher _inner;
    private readonly IChannelRateLimiter _rateLimiter;
    private readonly IRateLimitMetricsService _metrics;
    private readonly IAppLogger<ThrottledChannelDispatcher> _logger;

    public ThrottledChannelDispatcher(
        IChannelDispatcher inner,
        IChannelRateLimiterRegistry rateLimiterRegistry,
        IRateLimitMetricsService metrics,
        IAppLogger<ThrottledChannelDispatcher> logger)
    {
        _inner = inner;
        _rateLimiter = rateLimiterRegistry.GetLimiter(inner.Channel);
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public ChannelType Channel => _inner.Channel;

    /// <summary>
    /// Acquires a rate-limit token, then delegates to the inner dispatcher.
    ///
    /// Queue backpressure (TASK-022-04):
    ///   When the token bucket is exhausted, WaitAsync blocks the caller until
    ///   a token becomes available. Multiple concurrent senders will queue up
    ///   behind the semaphore rather than overwhelming the channel.
    ///
    /// Rate limit exceeded (graceful handling):
    ///   If the wait times out (OperationCanceledException from rate limiter),
    ///   returns a transient DispatchResult — the retry policy will retry later.
    /// </summary>
    public async Task<DispatchResult> SendAsync(
        DispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        // Unlimited channel (NoOpRateLimiter) returns immediately
        if (_rateLimiter.TokensPerSecond == 0)
        {
            _metrics.RecordTokenAcquired(Channel);
            return await _inner.SendAsync(request, cancellationToken);
        }

        var waitStart = DateTime.UtcNow;

        try
        {
            await _rateLimiter.WaitAsync(cancellationToken: cancellationToken);

            var waitDuration = DateTime.UtcNow - waitStart;
            if (waitDuration > TimeSpan.FromMilliseconds(10))
            {
                // We waited — record throttle event
                _metrics.RecordThrottleWait(Channel, waitDuration);
                _logger.LogDebug(
                    "Channel {Channel}: throttle wait {WaitMs}ms (available tokens: {Tokens:F1}, waiting: {Waiting})",
                    Channel,
                    (int)waitDuration.TotalMilliseconds,
                    _rateLimiter.AvailableTokens,
                    _rateLimiter.WaitingCount);
            }
            else
            {
                _metrics.RecordTokenAcquired(Channel);
            }

            return await _inner.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Rate limiter timed out (not a user-requested cancellation)
            // This is a transient failure — the retry policy will retry with backoff.
            _metrics.RecordRateLimitExceeded(Channel);

            _logger.LogWarning(
                "Channel {Channel}: rate limit exceeded for recipient {Recipient}. " +
                "WaitingCount={Waiting} AvailableTokens={Tokens:F1}. " +
                "Returning transient failure for retry.",
                Channel,
                request.Recipient.Email ?? request.Recipient.PhoneNumber ?? "unknown",
                _rateLimiter.WaitingCount,
                _rateLimiter.AvailableTokens);

            return DispatchResult.Fail(
                $"Rate limit exceeded for channel {Channel}: " +
                $"send rate of {_rateLimiter.TokensPerSecond} msg/sec exceeded. {ex.Message}",
                isTransient: true);
        }
    }
}
