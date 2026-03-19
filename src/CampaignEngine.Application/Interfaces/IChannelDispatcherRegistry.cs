using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Registry that resolves the appropriate IChannelDispatcher for a given channel type.
/// Uses DI-based strategy pattern — no hardcoded switch/case.
/// New channels can be added by registering a new IChannelDispatcher implementation in DI.
/// </summary>
public interface IChannelDispatcherRegistry
{
    /// <summary>
    /// Returns the dispatcher registered for the specified channel type.
    /// </summary>
    /// <param name="channel">The target communication channel.</param>
    /// <returns>The dispatcher for that channel.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no dispatcher is registered for the requested channel.
    /// </exception>
    IChannelDispatcher GetDispatcher(ChannelType channel);

    /// <summary>
    /// Returns true if a dispatcher is registered for the specified channel.
    /// </summary>
    bool HasDispatcher(ChannelType channel);

    /// <summary>
    /// Returns all registered channel types.
    /// </summary>
    IEnumerable<ChannelType> RegisteredChannels { get; }
}
