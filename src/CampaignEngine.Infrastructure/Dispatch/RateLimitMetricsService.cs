using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.Extensions.Options;
using CampaignEngine.Infrastructure.Configuration;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// In-memory implementation of <see cref="IRateLimitMetricsService"/>.
///
/// Thread-safe counters updated on each dispatch event. Metrics are exposed
/// via a REST endpoint (GET /api/admin/rate-limit-metrics) without requiring
/// Prometheus or AppMetrics infrastructure.
///
/// Design decisions:
///   - Interlocked operations for counters (lock-free, high throughput).
///   - 60-second sliding window for current send rate estimation.
///   - Suitable for Windows Server + IIS deployment (no native dependencies).
///
/// US-022 TASK-022-05: Rate limit metrics.
/// </summary>
public sealed class RateLimitMetricsService : IRateLimitMetricsService
{
    private readonly IChannelRateLimiterRegistry _registry;
    private readonly RateLimitOptions _options;

    // Per-channel counters (thread-safe via Interlocked)
    private readonly Dictionary<ChannelType, ChannelCounters> _counters;

    // Window tracking for current send rate
    private DateTime _windowStart = DateTime.UtcNow;

    public RateLimitMetricsService(
        IChannelRateLimiterRegistry registry,
        IOptions<RateLimitOptions> options)
    {
        _registry = registry;
        _options = options.Value;

        _counters = new Dictionary<ChannelType, ChannelCounters>
        {
            [ChannelType.Email]  = new ChannelCounters(),
            [ChannelType.Sms]    = new ChannelCounters(),
            [ChannelType.Letter] = new ChannelCounters()
        };
    }

    /// <inheritdoc />
    public void RecordTokenAcquired(ChannelType channel)
    {
        GetCounters(channel).IncrementTokensAcquired();
    }

    /// <inheritdoc />
    public void RecordThrottleWait(ChannelType channel, TimeSpan waitDuration)
    {
        GetCounters(channel).RecordWait(waitDuration);
    }

    /// <inheritdoc />
    public void RecordRateLimitExceeded(ChannelType channel)
    {
        GetCounters(channel).IncrementExceeded();
    }

    /// <inheritdoc />
    public IReadOnlyList<ChannelRateLimitMetrics> GetSnapshot()
    {
        var now = DateTime.UtcNow;
        var windowDuration = (now - _windowStart).TotalSeconds;
        var result = new List<ChannelRateLimitMetrics>();

        foreach (var (channel, counters) in _counters)
        {
            var limiter = _registry.GetLimiter(channel);
            var acquired = counters.TokensAcquired;
            var currentRate = windowDuration > 0 ? acquired / windowDuration : 0.0;

            result.Add(new ChannelRateLimitMetrics
            {
                Channel = channel,
                ConfiguredRatePerSecond = limiter.TokensPerSecond,
                TokensAcquired = acquired,
                ThrottleWaitCount = counters.WaitCount,
                TotalWaitDuration = TimeSpan.FromMilliseconds(counters.TotalWaitMs),
                RateLimitExceededCount = counters.ExceededCount,
                CurrentSendRatePerSecond = Math.Round(currentRate, 2),
                WindowStartUtc = _windowStart,
                AvailableTokens = limiter.AvailableTokens == double.MaxValue ? -1 : Math.Round(limiter.AvailableTokens, 2),
                WaitingCount = limiter.WaitingCount
            });
        }

        return result;
    }

    /// <inheritdoc />
    public void Reset()
    {
        foreach (var counters in _counters.Values)
            counters.Reset();

        _windowStart = DateTime.UtcNow;
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private ChannelCounters GetCounters(ChannelType channel)
    {
        return _counters.TryGetValue(channel, out var c) ? c : new ChannelCounters();
    }

    // ----------------------------------------------------------------
    // Thread-safe counter group
    // ----------------------------------------------------------------

    private sealed class ChannelCounters
    {
        private long _tokensAcquired;
        private long _waitCount;
        private long _totalWaitMs;
        private long _exceededCount;

        public long TokensAcquired => Interlocked.Read(ref _tokensAcquired);
        public long WaitCount => Interlocked.Read(ref _waitCount);
        public long TotalWaitMs => Interlocked.Read(ref _totalWaitMs);
        public long ExceededCount => Interlocked.Read(ref _exceededCount);

        public void IncrementTokensAcquired() => Interlocked.Increment(ref _tokensAcquired);

        public void RecordWait(TimeSpan duration)
        {
            Interlocked.Increment(ref _waitCount);
            Interlocked.Add(ref _totalWaitMs, (long)duration.TotalMilliseconds);
        }

        public void IncrementExceeded() => Interlocked.Increment(ref _exceededCount);

        public void Reset()
        {
            Interlocked.Exchange(ref _tokensAcquired, 0);
            Interlocked.Exchange(ref _waitCount, 0);
            Interlocked.Exchange(ref _totalWaitMs, 0);
            Interlocked.Exchange(ref _exceededCount, 0);
        }
    }
}
