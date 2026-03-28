using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Token-bucket rate limiter for a single channel.
///
/// Each channel has an independent rate limiter that enforces the configured
/// messages-per-second limit. Callers acquire a token before each send attempt.
///
/// Business rules (US-022):
///   BR-1: Default rates: Email 100/sec, SMS 10/sec, Letter unlimited.
///   BR-2: Configuration is per-channel and per-environment via appsettings.
///   BR-3: When the bucket is exhausted, WaitAsync applies backpressure
///         (waits until the next token is available or the timeout elapses).
///   BR-4: A MaxWaitTime cap prevents indefinite blocking in runaway scenarios.
/// </summary>
public interface IChannelRateLimiter
{
    /// <summary>
    /// The channel this limiter is associated with.
    /// </summary>
    ChannelType Channel { get; }

    /// <summary>
    /// Configured maximum tokens (messages) per second.
    /// 0 means unlimited — WaitAsync returns immediately.
    /// </summary>
    int TokensPerSecond { get; }

    /// <summary>
    /// Waits until a send token is available, then acquires it.
    ///
    /// If the bucket is full (below the rate limit), returns immediately.
    /// If the bucket is exhausted, waits up to <paramref name="maxWait"/>.
    ///
    /// Applies queue backpressure: callers block rather than dropping messages.
    /// </summary>
    /// <param name="maxWait">Maximum time to wait for a token.
    /// When <c>default</c>, uses the configured default wait timeout.
    /// Throws <see cref="OperationCanceledException"/> if the timeout elapses
    /// before a token becomes available (treat as transient failure).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WaitAsync(TimeSpan maxWait = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current number of available tokens in the bucket.
    /// Used for monitoring and metrics.
    /// </summary>
    double AvailableTokens { get; }

    /// <summary>
    /// Current wait queue depth: number of callers waiting for a token.
    /// Used for backpressure metrics.
    /// </summary>
    int WaitingCount { get; }
}
