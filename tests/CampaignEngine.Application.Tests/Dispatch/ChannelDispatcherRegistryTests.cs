using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Dispatch;

namespace CampaignEngine.Application.Tests.Dispatch;

public class ChannelDispatcherRegistryTests
{
    // ----------------------------------------------------------------
    // Registry resolution
    // ----------------------------------------------------------------

    [Fact]
    public void GetDispatcher_RegisteredChannel_ReturnsCorrectDispatcher()
    {
        var emailDispatcher = MockChannelDispatcher.Success(ChannelType.Email);
        var registry = new ChannelDispatcherRegistry([emailDispatcher]);

        var resolved = registry.GetDispatcher(ChannelType.Email);

        resolved.Should().BeSameAs(emailDispatcher);
    }

    [Fact]
    public void GetDispatcher_UnregisteredChannel_ThrowsChannelDispatcherNotFoundException()
    {
        var registry = new ChannelDispatcherRegistry([]);

        var act = () => registry.GetDispatcher(ChannelType.Email);

        act.Should().Throw<ChannelDispatcherNotFoundException>()
            .WithMessage("*Email*");
    }

    [Fact]
    public void HasDispatcher_RegisteredChannel_ReturnsTrue()
    {
        var emailDispatcher = MockChannelDispatcher.Success(ChannelType.Email);
        var registry = new ChannelDispatcherRegistry([emailDispatcher]);

        registry.HasDispatcher(ChannelType.Email).Should().BeTrue();
    }

    [Fact]
    public void HasDispatcher_UnregisteredChannel_ReturnsFalse()
    {
        var registry = new ChannelDispatcherRegistry([]);

        registry.HasDispatcher(ChannelType.Sms).Should().BeFalse();
    }

    [Fact]
    public void RegisteredChannels_ReturnsAllRegisteredChannelTypes()
    {
        var dispatchers = new[]
        {
            MockChannelDispatcher.Success(ChannelType.Email),
            MockChannelDispatcher.Success(ChannelType.Sms)
        };
        var registry = new ChannelDispatcherRegistry(dispatchers);

        registry.RegisteredChannels.Should().BeEquivalentTo(
            new[] { ChannelType.Email, ChannelType.Sms });
    }

    // ----------------------------------------------------------------
    // Strategy pattern: multiple dispatchers
    // ----------------------------------------------------------------

    [Fact]
    public void GetDispatcher_WithMultipleRegistrations_ReturnsChannelSpecificDispatcher()
    {
        var emailDispatcher = MockChannelDispatcher.Success(ChannelType.Email);
        var smsDispatcher = MockChannelDispatcher.Success(ChannelType.Sms);
        var letterDispatcher = MockChannelDispatcher.Success(ChannelType.Letter);
        var registry = new ChannelDispatcherRegistry([emailDispatcher, smsDispatcher, letterDispatcher]);

        registry.GetDispatcher(ChannelType.Email).Should().BeSameAs(emailDispatcher);
        registry.GetDispatcher(ChannelType.Sms).Should().BeSameAs(smsDispatcher);
        registry.GetDispatcher(ChannelType.Letter).Should().BeSameAs(letterDispatcher);
    }

    // ----------------------------------------------------------------
    // Dispatch delegation (end-to-end through registry)
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDispatcher_ThenSendAsync_DelegatesRequestToDispatcher()
    {
        var mockDispatcher = MockChannelDispatcher.Success(ChannelType.Email);
        var registry = new ChannelDispatcherRegistry([mockDispatcher]);

        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = "<p>Hello</p>",
            Recipient = new RecipientInfo { Email = "test@example.com" }
        };

        var dispatcher = registry.GetDispatcher(ChannelType.Email);
        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
        mockDispatcher.CallCount.Should().Be(1);
        mockDispatcher.SentRequests[0].Should().BeSameAs(request);
    }

    [Fact]
    public async Task MockDispatcher_TransientFailure_ReturnsTransientResult()
    {
        var mockDispatcher = MockChannelDispatcher.TransientFailure(ChannelType.Sms, "SMS gateway timeout");
        var registry = new ChannelDispatcherRegistry([mockDispatcher]);

        var request = new DispatchRequest { Channel = ChannelType.Sms };
        var result = await registry.GetDispatcher(ChannelType.Sms).SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
        result.ErrorDetail.Should().Be("SMS gateway timeout");
    }

    [Fact]
    public async Task MockDispatcher_PermanentFailure_ReturnsNonTransientResult()
    {
        var mockDispatcher = MockChannelDispatcher.PermanentFailure(ChannelType.Email, "Invalid email address");
        var registry = new ChannelDispatcherRegistry([mockDispatcher]);

        var request = new DispatchRequest { Channel = ChannelType.Email };
        var result = await registry.GetDispatcher(ChannelType.Email).SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Be("Invalid email address");
    }

    // ----------------------------------------------------------------
    // Cancellation
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_WithCancelledToken_ThrowsOperationCancelled()
    {
        var mockDispatcher = MockChannelDispatcher.Success(ChannelType.Email);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await mockDispatcher.SendAsync(new DispatchRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
