using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Singleton registry that manages one <see cref="IChannelRateLimiter"/> per channel.
///
/// Limiters are created once at startup from <see cref="RateLimitOptions"/> and
/// reused across all requests/scopes. Because the TokenBucketRateLimiter is
/// thread-safe and stateful, it must be a Singleton to share the bucket state
/// across concurrent dispatchers.
///
/// US-022 TASK-022-02: Per-channel rate limit configuration.
/// </summary>
public sealed class ChannelRateLimiterRegistry : IChannelRateLimiterRegistry, IDisposable
{
    private readonly Dictionary<ChannelType, IChannelRateLimiter> _limiters;

    public ChannelRateLimiterRegistry(IOptions<RateLimitOptions> options)
    {
        var cfg = options.Value;
        _limiters = new Dictionary<ChannelType, IChannelRateLimiter>
        {
            [ChannelType.Email]  = CreateLimiter(ChannelType.Email, cfg.Email),
            [ChannelType.Sms]    = CreateLimiter(ChannelType.Sms, cfg.Sms),
            [ChannelType.Letter] = CreateLimiter(ChannelType.Letter, cfg.Letter)
        };
    }

    /// <inheritdoc />
    public IChannelRateLimiter GetLimiter(ChannelType channel)
    {
        return _limiters.TryGetValue(channel, out var limiter)
            ? limiter
            : new NoOpRateLimiter(channel);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IChannelRateLimiter> GetAllLimiters()
        => _limiters.Values;

    // ----------------------------------------------------------------
    // Factory
    // ----------------------------------------------------------------

    private static IChannelRateLimiter CreateLimiter(
        ChannelType channel,
        ChannelRateLimitOptions options)
    {
        if (options.TokensPerSecond <= 0)
            return new NoOpRateLimiter(channel);

        var maxWait = options.MaxWaitTimeSeconds > 0
            ? TimeSpan.FromSeconds(options.MaxWaitTimeSeconds)
            : TimeSpan.Zero;   // Zero = unlimited wait

        return new TokenBucketRateLimiter(
            channel,
            options.TokensPerSecond,
            options.BurstMultiplier,
            maxWait);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            if (limiter is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
