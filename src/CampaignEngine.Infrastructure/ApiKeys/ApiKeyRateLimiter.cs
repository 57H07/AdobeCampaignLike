using System.Collections.Concurrent;
using CampaignEngine.Application.Interfaces;

namespace CampaignEngine.Infrastructure.ApiKeys;

/// <summary>
/// In-memory, per-API-key rate limiter using a sliding 1-minute window.
///
/// Design:
///   - One <see cref="SlidingWindowCounter"/> per API key ID stored in a ConcurrentDictionary.
///   - Each counter holds a queue of UTC timestamps of accepted requests within the last 60 s.
///   - On each TryAcquireAsync call the counter is purged of timestamps older than 60 s, then
///     the remaining count is compared with the per-key limit.
///   - Thread safety: each SlidingWindowCounter uses a lightweight lock on its own state.
///
/// Trade-offs:
///   - Fully in-process — suitable for single-instance IIS deployments.
///   - For multi-instance deployments a distributed cache (Redis) backed implementation
///     implementing the same <see cref="IApiKeyRateLimiter"/> interface can be substituted.
///
/// US-033: BR-1 Default 1000 req/min; BR-2 sliding 1-minute window.
/// </summary>
public sealed class ApiKeyRateLimiter : IApiKeyRateLimiter
{
    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(1);

    // Per-key sliding window counters (never removed — API keys are never hard-deleted).
    private readonly ConcurrentDictionary<Guid, SlidingWindowCounter> _counters = new();

    /// <inheritdoc />
    public Task<RateLimitResult> TryAcquireAsync(
        Guid apiKeyId,
        int rateLimitPerMinute,
        CancellationToken cancellationToken = default)
    {
        var counter = _counters.GetOrAdd(apiKeyId, _ => new SlidingWindowCounter(rateLimitPerMinute));

        // Update the configured limit in case it was changed after the counter was first created.
        counter.UpdateLimit(rateLimitPerMinute);

        var now = DateTime.UtcNow;
        var (allowed, remaining, resetAt) = counter.TryRecord(now);

        var result = new RateLimitResult
        {
            IsAllowed = allowed,
            Limit = rateLimitPerMinute,
            Remaining = remaining,
            ResetAt = resetAt
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public ApiKeyRateLimitStats? GetStats(Guid apiKeyId)
    {
        if (!_counters.TryGetValue(apiKeyId, out var counter))
            return null;

        return counter.GetStats(apiKeyId);
    }

    /// <inheritdoc />
    public IReadOnlyList<ApiKeyRateLimitStats> GetAllStats()
    {
        return _counters
            .Select(kvp => kvp.Value.GetStats(kvp.Key))
            .ToList();
    }

    /// <inheritdoc />
    public void RecordRejected(Guid apiKeyId)
    {
        if (_counters.TryGetValue(apiKeyId, out var counter))
            counter.IncrementRejected();
    }

    // ----------------------------------------------------------------
    // Inner class: per-key sliding-window counter
    // ----------------------------------------------------------------

    private sealed class SlidingWindowCounter
    {
        private readonly object _lock = new();

        // Queue of accepted-request timestamps within the current window.
        private readonly Queue<DateTime> _timestamps = new();

        private int _limitPerMinute;
        private long _totalRequests;
        private long _totalRejected;

        public SlidingWindowCounter(int limitPerMinute)
        {
            _limitPerMinute = limitPerMinute;
        }

        /// <summary>
        /// Updates the per-key limit if it was changed after creation.
        /// </summary>
        public void UpdateLimit(int newLimit)
        {
            Interlocked.Exchange(ref _limitPerMinute, newLimit);
        }

        /// <summary>
        /// Attempts to record a new request at <paramref name="now"/>.
        /// Returns (allowed, remaining, windowResetAt).
        /// </summary>
        public (bool allowed, int remaining, DateTime resetAt) TryRecord(DateTime now)
        {
            var limit = _limitPerMinute;
            var windowStart = now - WindowSize;

            lock (_lock)
            {
                // Evict timestamps older than the window.
                while (_timestamps.Count > 0 && _timestamps.Peek() <= windowStart)
                    _timestamps.Dequeue();

                // Window reset time = oldest accepted request + 60 s (or now + 60 s if window is empty).
                var resetAt = _timestamps.Count > 0
                    ? _timestamps.Peek() + WindowSize
                    : now + WindowSize;

                if (_timestamps.Count >= limit)
                {
                    // Limit reached — do NOT add to queue, request is rejected.
                    var remaining = 0;
                    Interlocked.Increment(ref _totalRejected);
                    return (false, remaining, resetAt);
                }

                // Accept the request.
                _timestamps.Enqueue(now);
                Interlocked.Increment(ref _totalRequests);

                var remainingAfter = Math.Max(0, limit - _timestamps.Count);
                return (true, remainingAfter, resetAt);
            }
        }

        public void IncrementRejected() => Interlocked.Increment(ref _totalRejected);

        public ApiKeyRateLimitStats GetStats(Guid apiKeyId)
        {
            var limit = _limitPerMinute;
            var now = DateTime.UtcNow;
            var windowStart = now - WindowSize;

            int windowCount;
            DateTime resetAt;

            lock (_lock)
            {
                // Count timestamps in the current window without evicting
                // (read-only — GetStats is observational, not transactional).
                windowCount = _timestamps.Count(ts => ts > windowStart);
                resetAt = _timestamps.Count > 0
                    ? _timestamps.Peek() + WindowSize
                    : now + WindowSize;
            }

            return new ApiKeyRateLimitStats
            {
                ApiKeyId = apiKeyId,
                LimitPerMinute = limit,
                TotalRequests = Interlocked.Read(ref _totalRequests),
                TotalRejected = Interlocked.Read(ref _totalRejected),
                RequestsInCurrentWindow = windowCount,
                CurrentWindowResetAt = resetAt
            };
        }
    }
}
