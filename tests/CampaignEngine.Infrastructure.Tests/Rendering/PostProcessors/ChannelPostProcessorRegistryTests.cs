using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Rendering.PostProcessors;

namespace CampaignEngine.Infrastructure.Tests.Rendering.PostProcessors;

/// <summary>
/// Unit tests for ChannelPostProcessorRegistry.
/// Covers: strategy pattern resolution, HasProcessor, GetProcessor, exception on missing channel.
/// </summary>
public class ChannelPostProcessorRegistryTests
{
    private static IChannelPostProcessor CreateMockProcessor(ChannelType channel)
    {
        var mock = new Mock<IChannelPostProcessor>();
        mock.Setup(p => p.Channel).Returns(channel);
        mock.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<PostProcessingContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostProcessingResult.Text("output"));
        return mock.Object;
    }

    [Fact]
    public void GetProcessor_RegisteredChannel_ReturnsCorrectProcessor()
    {
        var emailProcessor = CreateMockProcessor(ChannelType.Email);
        var registry = new ChannelPostProcessorRegistry(new[] { emailProcessor });

        var result = registry.GetProcessor(ChannelType.Email);

        result.Should().BeSameAs(emailProcessor);
    }

    [Fact]
    public void GetProcessor_UnregisteredChannel_ThrowsInvalidOperationException()
    {
        var registry = new ChannelPostProcessorRegistry(Array.Empty<IChannelPostProcessor>());

        var act = () => registry.GetProcessor(ChannelType.Email);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Email*");
    }

    [Fact]
    public void HasProcessor_RegisteredChannel_ReturnsTrue()
    {
        var processor = CreateMockProcessor(ChannelType.Sms);
        var registry = new ChannelPostProcessorRegistry(new[] { processor });

        registry.HasProcessor(ChannelType.Sms).Should().BeTrue();
    }

    [Fact]
    public void HasProcessor_UnregisteredChannel_ReturnsFalse()
    {
        var processor = CreateMockProcessor(ChannelType.Email);
        var registry = new ChannelPostProcessorRegistry(new[] { processor });

        registry.HasProcessor(ChannelType.Sms).Should().BeFalse();
    }

    [Fact]
    public void RegisteredChannels_ReturnsAllRegisteredChannelTypes()
    {
        var processors = new[]
        {
            CreateMockProcessor(ChannelType.Email),
            CreateMockProcessor(ChannelType.Letter),
            CreateMockProcessor(ChannelType.Sms)
        };

        var registry = new ChannelPostProcessorRegistry(processors);

        registry.RegisteredChannels.Should().BeEquivalentTo(
            new[] { ChannelType.Email, ChannelType.Letter, ChannelType.Sms });
    }

    [Fact]
    public void Constructor_EmptyProcessors_CreatesEmptyRegistry()
    {
        var registry = new ChannelPostProcessorRegistry(Array.Empty<IChannelPostProcessor>());

        registry.RegisteredChannels.Should().BeEmpty();
    }

    [Fact]
    public void GetProcessor_MultipleChannels_ResolvesCorrectProcessor()
    {
        var emailProcessor = CreateMockProcessor(ChannelType.Email);
        var smsProcessor = CreateMockProcessor(ChannelType.Sms);
        var letterProcessor = CreateMockProcessor(ChannelType.Letter);

        var registry = new ChannelPostProcessorRegistry(
            new[] { emailProcessor, smsProcessor, letterProcessor });

        registry.GetProcessor(ChannelType.Email).Should().BeSameAs(emailProcessor);
        registry.GetProcessor(ChannelType.Sms).Should().BeSameAs(smsProcessor);
        registry.GetProcessor(ChannelType.Letter).Should().BeSameAs(letterProcessor);
    }
}
