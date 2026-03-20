using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Registry for resolving IChannelPostProcessor implementations by channel type.
/// Uses the same DI-based strategy pattern as IChannelDispatcherRegistry.
/// </summary>
public interface IChannelPostProcessorRegistry
{
    /// <summary>
    /// Returns the registered channel types that have a post-processor.
    /// </summary>
    IEnumerable<ChannelType> RegisteredChannels { get; }

    /// <summary>
    /// Resolves the post-processor for a given channel type.
    /// </summary>
    /// <param name="channel">Channel type to look up.</param>
    /// <returns>The registered IChannelPostProcessor for that channel.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no processor is registered for the channel.</exception>
    IChannelPostProcessor GetProcessor(ChannelType channel);

    /// <summary>
    /// Returns true if a post-processor is registered for the given channel type.
    /// </summary>
    bool HasProcessor(ChannelType channel);
}
