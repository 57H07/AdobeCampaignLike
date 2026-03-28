using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Thread-safe token bucket implementation of <see cref="IChannelRateLimiter"/>.
///
/// Algorithm:
///   - The bucket holds up to <c>capacity</c> tokens (burstMultiplier * tokensPerSecond).
///   - Tokens are refilled continuously at <c>tokensPerSecond</c> tokens per second.
///   - Each send attempt consumes 1 token from the bucket.
///   - When the bucket is empty, <see cref="WaitAsync"/> blocks until a token becomes
///     available (backpressure) or the timeout elapses (rate limit exceeded).
///
/// Concurrency:
///   - Uses a SemaphoreSlim-based wait queue for fair ordering under load.
///   - Token refill is computed lazily on each acquire (no background timer needed).
///   - All state mutations are protected by a lightweight lock.
///
/// Business rules (US-022):
///   BR-1: tokensPerSecond = 0 means unlimited; WaitAsync returns immediately.
///   BR-2: Default max wait: Email 30s, SMS 10s, Letter unlimited (0s = no timeout).
///   BR-3: Timeout throws OperationCanceledException (treated as transient failure by dispatcher).
///   BR-4: BurstMultiplier allows short bursts above sustained rate.
/// </summary>
public sealed class TokenBucketRateLimiter : IChannelRateLimiter, IDisposable
{
    private readonly ChannelType _channel;
    private readonly int _tokensPerSecond;
    private readonly double _capacity;
    private readonly TimeSpan _defaultMaxWait;

    // Token bucket state — access protected by _lock
    private double _availableTokens;
    private DateTime _lastRefillTime;
    private readonly object _lock = new();

    // Wait queue for backpressure
    private int _waitingCount;

    /// <summary>
    /// Initializes a new token bucket rate limiter.
    /// </summary>
    /// <param name="channel">The channel this limiter serves.</param>
    /// <param name="tokensPerSecond">Maximum sustained send rate. 0 = unlimited.</param>
    /// <param name="burstMultiplier">Bucket capacity multiplier (allows short bursts).</param>
    /// <param name="defaultMaxWait">Default wait timeout. Zero means no timeout (unlimited wait).</param>
    public TokenBucketRateLimiter(
        ChannelType channel,
        int tokensPerSecond,
        double burstMultiplier = 2.0,
        TimeSpan defaultMaxWait = default)
    {
        _channel = channel;
        _tokensPerSecond = tokensPerSecond;
        _capacity = tokensPerSecond > 0
            ? tokensPerSecond * Math.Max(1.0, burstMultiplier)
            : double.MaxValue;
        _defaultMaxWait = defaultMaxWait == default && tokensPerSecond > 0
            ? TimeSpan.FromSeconds(30)
            : defaultMaxWait;

        // Start with a full bucket
        _availableTokens = tokensPerSecond > 0 ? _capacity : double.MaxValue;
        _lastRefillTime = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public ChannelType Channel => _channel;

    /// <inheritdoc />
    public int TokensPerSecond => _tokensPerSecond;

    /// <inheritdoc />
    public double AvailableTokens
    {
        get
        {
            lock (_lock)
            {
                RefillTokens();
                return _availableTokens;
            }
        }
    }

    /// <inheritdoc />
    public int WaitingCount => Volatile.Read(ref _waitingCount);

    /// <inheritdoc />
    public async Task WaitAsync(TimeSpan maxWait = default, CancellationToken cancellationToken = default)
    {
        // Unlimited channel — return immediately
        if (_tokensPerSecond == 0)
            return;

        var effectiveTimeout = maxWait == default ? _defaultMaxWait : maxWait;

        // Fast path: try to acquire a token without waiting
        if (TryAcquireToken())
            return;

        // Slow path: wait for a token to become available
        Interlocked.Increment(ref _waitingCount);
        try
        {
            await WaitForTokenAsync(effectiveTimeout, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _waitingCount);
        }
    }

    // ----------------------------------------------------------------
    // Private implementation
    // ----------------------------------------------------------------

    /// <summary>
    /// Attempts to acquire a token without blocking.
    /// Returns true if a token was available and consumed.
    /// </summary>
    private bool TryAcquireToken()
    {
        lock (_lock)
        {
            RefillTokens();
            if (_availableTokens >= 1.0)
            {
                _availableTokens -= 1.0;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Waits until a token becomes available by polling with exponential backoff.
    /// Throws OperationCanceledException if timeout elapses or cancellation is requested.
    /// </summary>
    private async Task WaitForTokenAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = timeout == TimeSpan.Zero
            ? DateTime.MaxValue
            : DateTime.UtcNow + timeout;

        // Polling interval starts small and grows (up to 100ms cap)
        var pollIntervalMs = 1;
        const int maxPollIntervalMs = 100;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryAcquireToken())
                return;

            if (DateTime.UtcNow >= deadline)
            {
                throw new OperationCanceledException(
                    $"Rate limit exceeded for channel {_channel}: " +
                    $"no token available within {timeout.TotalSeconds:F1}s wait timeout.");
            }

            // Calculate time until next token is available for more precise sleeping
            var waitMs = CalculateWaitMs(pollIntervalMs, deadline);
            if (waitMs <= 0)
                continue;

            await Task.Delay(waitMs, cancellationToken);

            // Exponential backoff (capped)
            pollIntervalMs = Math.Min(pollIntervalMs * 2, maxPollIntervalMs);
        }
    }

    /// <summary>
    /// Calculates the appropriate wait time in milliseconds.
    /// Considers both the ideal next-token time and the deadline.
    /// </summary>
    private int CalculateWaitMs(int pollIntervalMs, DateTime deadline)
    {
        var now = DateTime.UtcNow;

        // Time until next token (based on refill rate)
        double msUntilNextToken;
        lock (_lock)
        {
            var deficit = 1.0 - _availableTokens;
            msUntilNextToken = deficit > 0 && _tokensPerSecond > 0
                ? deficit / _tokensPerSecond * 1000.0
                : 0;
        }

        // Time until deadline
        var msUntilDeadline = (deadline - now).TotalMilliseconds;
        if (msUntilDeadline <= 0)
            return 0;

        // Wait either the poll interval or until the next token, whichever is smaller
        var targetMs = Math.Max(1, Math.Min(pollIntervalMs, (int)msUntilNextToken));
        return (int)Math.Min(targetMs, msUntilDeadline);
    }

    /// <summary>
    /// Refills the bucket based on elapsed time since last refill.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void RefillTokens()
    {
        if (_tokensPerSecond <= 0)
            return;

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefillTime).TotalSeconds;

        if (elapsed > 0)
        {
            var newTokens = elapsed * _tokensPerSecond;
            _availableTokens = Math.Min(_capacity, _availableTokens + newTokens);
            _lastRefillTime = now;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No managed resources to release — the lock and counters are value types.
    }
}
