using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Tests.Dispatch;

/// <summary>
/// A configurable mock dispatcher for use in unit and integration tests.
/// Captures all dispatch requests without performing actual I/O.
/// Can be configured to simulate success, permanent failure, or transient failure.
/// </summary>
public sealed class MockChannelDispatcher : IChannelDispatcher
{
    private readonly DispatchResult _result;

    public MockChannelDispatcher(ChannelType channel, DispatchResult? result = null)
    {
        Channel = channel;
        _result = result ?? DispatchResult.Ok($"mock-{Guid.NewGuid()}");
    }

    /// <inheritdoc/>
    public ChannelType Channel { get; }

    /// <summary>
    /// All requests that were dispatched through this mock.
    /// Use in tests to assert what was sent.
    /// </summary>
    public List<DispatchRequest> SentRequests { get; } = [];

    /// <summary>
    /// Number of times SendAsync was called.
    /// </summary>
    public int CallCount => SentRequests.Count;

    /// <inheritdoc/>
    public Task<DispatchResult> SendAsync(DispatchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SentRequests.Add(request);
        return Task.FromResult(_result);
    }

    /// <summary>
    /// Creates a mock dispatcher that always returns success.
    /// </summary>
    public static MockChannelDispatcher Success(ChannelType channel) =>
        new(channel, DispatchResult.Ok($"mock-msg-{Guid.NewGuid()}"));

    /// <summary>
    /// Creates a mock dispatcher that returns a permanent failure.
    /// </summary>
    public static MockChannelDispatcher PermanentFailure(ChannelType channel, string errorDetail = "Permanent error") =>
        new(channel, DispatchResult.Fail(errorDetail, isTransient: false));

    /// <summary>
    /// Creates a mock dispatcher that returns a transient failure.
    /// </summary>
    public static MockChannelDispatcher TransientFailure(ChannelType channel, string errorDetail = "Transient error") =>
        new(channel, DispatchResult.Fail(errorDetail, isTransient: true));
}
