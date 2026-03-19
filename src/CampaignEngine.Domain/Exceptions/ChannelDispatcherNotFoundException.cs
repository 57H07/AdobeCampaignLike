namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Thrown when no dispatcher is registered for the requested channel type.
/// Indicates a configuration or registration issue in the DI container.
/// </summary>
public class ChannelDispatcherNotFoundException : DomainException
{
    public ChannelDispatcherNotFoundException(string channel)
        : base($"No dispatcher registered for channel '{channel}'. Register an IChannelDispatcher implementation in the DI container.")
    {
        Channel = channel;
    }

    public string Channel { get; }
}
