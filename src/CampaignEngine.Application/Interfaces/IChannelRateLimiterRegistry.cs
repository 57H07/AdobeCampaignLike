using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Registry that resolves the rate limiter for a given channel at runtime.
///
/// Returns the configured <see cref="IChannelRateLimiter"/> for the specified channel,
/// or a no-op limiter (unlimited) when no throttle is configured.
/// </summary>
public interface IChannelRateLimiterRegistry
{
    /// <summary>
    /// Returns the rate limiter for the specified channel.
    /// Never returns null — returns a no-op (unlimited) limiter for channels without config.
    /// </summary>
    IChannelRateLimiter GetLimiter(ChannelType channel);

    /// <summary>
    /// Returns all registered rate limiters (used for metrics endpoints).
    /// </summary>
    IReadOnlyCollection<IChannelRateLimiter> GetAllLimiters();
}
