using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// DI-based registry that resolves the appropriate IChannelDispatcher at runtime.
/// All registered IChannelDispatcher implementations are injected via IEnumerable,
/// enabling the strategy pattern without any hardcoded switch/case statements.
///
/// To add a new channel:
/// 1. Create a class implementing IChannelDispatcher
/// 2. Register it in DI: services.AddScoped&lt;IChannelDispatcher, YourDispatcher&gt;()
/// 3. No changes required to this registry or any other core code.
/// </summary>
public sealed class ChannelDispatcherRegistry : IChannelDispatcherRegistry
{
    private readonly IReadOnlyDictionary<ChannelType, IChannelDispatcher> _dispatchers;

    public ChannelDispatcherRegistry(IEnumerable<IChannelDispatcher> dispatchers)
    {
        _dispatchers = dispatchers.ToDictionary(d => d.Channel, d => d);
    }

    /// <inheritdoc/>
    public IEnumerable<ChannelType> RegisteredChannels => _dispatchers.Keys;

    /// <inheritdoc/>
    public IChannelDispatcher GetDispatcher(ChannelType channel)
    {
        if (_dispatchers.TryGetValue(channel, out var dispatcher))
        {
            return dispatcher;
        }

        throw new ChannelDispatcherNotFoundException(channel.ToString());
    }

    /// <inheritdoc/>
    public bool HasDispatcher(ChannelType channel) => _dispatchers.ContainsKey(channel);
}
