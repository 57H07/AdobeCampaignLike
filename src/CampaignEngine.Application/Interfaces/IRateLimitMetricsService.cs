using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// In-memory metrics service for rate limiting telemetry.
///
/// Records throttle events and provides aggregated send-rate metrics
/// for the admin monitoring endpoint (US-022 TASK-022-05).
///
/// Design: lightweight in-memory counters updated on each dispatch attempt.
/// No Prometheus or AppMetrics dependency — metrics exposed via a REST endpoint
/// suitable for Windows Server + IIS deployment.
/// </summary>
public interface IRateLimitMetricsService
{
    /// <summary>
    /// Records that a token was successfully acquired (send proceeded).
    /// </summary>
    void RecordTokenAcquired(ChannelType channel);

    /// <summary>
    /// Records that a caller waited for a token (backpressure applied).
    /// </summary>
    void RecordThrottleWait(ChannelType channel, TimeSpan waitDuration);

    /// <summary>
    /// Records that a caller timed out waiting for a token (rate limit exceeded error).
    /// </summary>
    void RecordRateLimitExceeded(ChannelType channel);

    /// <summary>
    /// Returns the current metrics snapshot for all channels.
    /// </summary>
    IReadOnlyList<ChannelRateLimitMetrics> GetSnapshot();

    /// <summary>
    /// Resets all counters (admin operation).
    /// </summary>
    void Reset();
}

/// <summary>
/// Metrics snapshot for a single channel.
/// </summary>
public sealed record ChannelRateLimitMetrics
{
    public required ChannelType Channel { get; init; }

    /// <summary>Configured rate limit (tokens/second). 0 = unlimited.</summary>
    public required int ConfiguredRatePerSecond { get; init; }

    /// <summary>Total tokens acquired since last reset.</summary>
    public required long TokensAcquired { get; init; }

    /// <summary>Number of times a caller had to wait for a token.</summary>
    public required long ThrottleWaitCount { get; init; }

    /// <summary>Total cumulative time callers spent waiting for tokens.</summary>
    public required TimeSpan TotalWaitDuration { get; init; }

    /// <summary>Average wait duration per throttle event.</summary>
    public TimeSpan AverageWaitDuration =>
        ThrottleWaitCount > 0
            ? TimeSpan.FromTicks(TotalWaitDuration.Ticks / ThrottleWaitCount)
            : TimeSpan.Zero;

    /// <summary>Number of times the rate limit was exceeded (timeout while waiting).</summary>
    public required long RateLimitExceededCount { get; init; }

    /// <summary>Approximate current send rate (tokens/sec) based on recent window.</summary>
    public required double CurrentSendRatePerSecond { get; init; }

    /// <summary>Metrics window start time (UTC).</summary>
    public required DateTime WindowStartUtc { get; init; }

    /// <summary>Current available tokens in the bucket.</summary>
    public required double AvailableTokens { get; init; }

    /// <summary>Current number of callers waiting for a token.</summary>
    public required int WaitingCount { get; init; }
}
