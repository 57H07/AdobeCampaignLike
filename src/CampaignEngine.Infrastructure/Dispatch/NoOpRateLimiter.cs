using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// No-op implementation of <see cref="IChannelRateLimiter"/> for unlimited channels.
///
/// Used for the Letter channel (no rate limit per BR-1) and for any channel
/// whose TokensPerSecond is configured as 0.
///
/// All calls to WaitAsync return immediately without delay.
/// </summary>
public sealed class NoOpRateLimiter : IChannelRateLimiter
{
    public NoOpRateLimiter(ChannelType channel)
    {
        Channel = channel;
    }

    /// <inheritdoc />
    public ChannelType Channel { get; }

    /// <inheritdoc />
    public int TokensPerSecond => 0;

    /// <inheritdoc />
    public double AvailableTokens => double.MaxValue;

    /// <inheritdoc />
    public int WaitingCount => 0;

    /// <inheritdoc />
    public Task WaitAsync(TimeSpan maxWait = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
