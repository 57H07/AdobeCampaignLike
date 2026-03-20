using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Rendering.PostProcessors;

/// <summary>
/// DI-based registry that resolves the appropriate IChannelPostProcessor at runtime.
/// Mirrors the IChannelDispatcherRegistry pattern: all registered IChannelPostProcessor
/// implementations are injected via IEnumerable, enabling the strategy pattern.
///
/// To add a new channel post-processor:
/// 1. Implement IChannelPostProcessor for the new channel.
/// 2. Register it in DI: services.AddScoped&lt;IChannelPostProcessor, YourPostProcessor&gt;()
/// 3. No changes required to this registry.
/// </summary>
public sealed class ChannelPostProcessorRegistry : IChannelPostProcessorRegistry
{
    private readonly IReadOnlyDictionary<ChannelType, IChannelPostProcessor> _processors;

    public ChannelPostProcessorRegistry(IEnumerable<IChannelPostProcessor> processors)
    {
        _processors = processors.ToDictionary(p => p.Channel);
    }

    /// <inheritdoc/>
    public IEnumerable<ChannelType> RegisteredChannels => _processors.Keys;

    /// <inheritdoc/>
    public IChannelPostProcessor GetProcessor(ChannelType channel)
    {
        if (_processors.TryGetValue(channel, out var processor))
        {
            return processor;
        }

        throw new InvalidOperationException(
            $"No IChannelPostProcessor registered for channel '{channel}'. " +
            $"Register an implementation in DI: services.AddScoped<IChannelPostProcessor, YourProcessor>()");
    }

    /// <inheritdoc/>
    public bool HasProcessor(ChannelType channel) => _processors.ContainsKey(channel);
}
