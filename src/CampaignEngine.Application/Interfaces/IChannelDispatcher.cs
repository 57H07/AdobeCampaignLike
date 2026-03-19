using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Strategy interface for channel-specific message dispatchers.
/// Each channel (Email, Letter, SMS) implements this interface.
/// Registered in DI container; selected by ChannelType at runtime.
/// </summary>
public interface IChannelDispatcher
{
    /// <summary>
    /// The channel type this dispatcher handles.
    /// </summary>
    ChannelType Channel { get; }

    /// <summary>
    /// Dispatches a single message to the specified recipient.
    /// Transient failures should throw retriable exceptions.
    /// </summary>
    Task<DispatchResult> SendAsync(DispatchRequest request, CancellationToken cancellationToken = default);
}
