using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.DTOs.Send;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Services;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Dispatch;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Rendering;
using CampaignEngine.Infrastructure.Send;
using CampaignEngine.Infrastructure.Tests.Persistence;

namespace CampaignEngine.Infrastructure.Tests.Send;

/// <summary>
/// Integration tests for SingleSendService using an in-memory database.
/// Covers the full orchestration path: template resolution → validation → rendering → dispatch → logging.
/// </summary>
public class SingleSendServiceTests : DbContextTestBase
{
    // ----------------------------------------------------------------
    // Helpers — local dispatcher mock
    // ----------------------------------------------------------------

    /// <summary>A simple local dispatcher that records calls without performing I/O.</summary>
    private sealed class LocalMockDispatcher : IChannelDispatcher
    {
        private readonly DispatchResult _result;
        public ChannelType Channel { get; }
        public List<DispatchRequest> SentRequests { get; } = [];
        public int CallCount => SentRequests.Count;

        public LocalMockDispatcher(ChannelType channel, DispatchResult? result = null)
        {
            Channel = channel;
            _result = result ?? DispatchResult.Ok($"mock-{Guid.NewGuid()}");
        }

        public Task<DispatchResult> SendAsync(DispatchRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SentRequests.Add(request);
            return Task.FromResult(_result);
        }

        public static LocalMockDispatcher Success(ChannelType ch) => new(ch);
        public static LocalMockDispatcher PermanentFailure(ChannelType ch, string error) =>
            new(ch, DispatchResult.Fail(error, isTransient: false));
    }

    // ----------------------------------------------------------------
    // Helpers — build a configured service instance
    // ----------------------------------------------------------------

    private SingleSendService BuildService(
        IChannelDispatcher? dispatcher = null,
        ISendRequestValidator? validator = null,
        ISendLogService? sendLogService = null)
    {
        var emailDispatcher = dispatcher ?? LocalMockDispatcher.Success(ChannelType.Email);
        var registry = new ChannelDispatcherRegistry([emailDispatcher]);
        var renderer = new ScribanTemplateRenderer(
            new Mock<Microsoft.Extensions.Logging.ILogger<ScribanTemplateRenderer>>().Object);
        var val = validator ?? new SendRequestValidator();
        var logService = sendLogService ?? CreateRealSendLogService();
        var logger = new Mock<IAppLogger<SingleSendService>>();

        return new SingleSendService(
            new TemplateRepository(Context),
            renderer,
            registry,
            val,
            logService,
            logger.Object);
    }

    private ISendLogService CreateRealSendLogService()
    {
        var logLogger = new Mock<IAppLogger<SendLogService>>();
        var sendLogRepository = new SendLogRepository(Context);
        var unitOfWork = new UnitOfWork(Context);
        return new SendLogService(sendLogRepository, unitOfWork, logLogger.Object);
    }

    private async Task<Template> SeedPublishedEmailTemplateAsync(params string[] placeholderKeys)
    {
        var template = new Template
        {
            Name = "Test Email Template",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Published,
            BodyPath = "templates/test-email/v1.html",
            PlaceholderManifests = placeholderKeys
                .Select(k => new PlaceholderManifestEntry { Key = k, Type = PlaceholderType.Scalar })
                .ToList()
        };
        Context.Templates.Add(template);
        await Context.SaveChangesAsync();
        return template;
    }

    private async Task<Template> SeedDraftEmailTemplateAsync()
    {
        var template = new Template
        {
            Name = "Draft Email Template",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Draft,
            BodyPath = "templates/draft-email/v1.html",
            PlaceholderManifests = new List<PlaceholderManifestEntry>()
        };
        Context.Templates.Add(template);
        await Context.SaveChangesAsync();
        return template;
    }

    // ----------------------------------------------------------------
    // Happy path
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ValidRequest_ReturnsSuccessResponse()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");
        var sut = BuildService();

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var response = await sut.SendAsync(request);

        response.Success.Should().BeTrue();
        response.Status.Should().Be(SendStatus.Sent);
        response.Channel.Should().Be(ChannelType.Email);
        response.TrackingId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SendAsync_ValidRequest_TrackingIdIsUnique()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");
        var sut = BuildService();

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var response1 = await sut.SendAsync(request);
        var response2 = await sut.SendAsync(request);

        response1.TrackingId.Should().NotBe(response2.TrackingId);
    }

    [Fact]
    public async Task SendAsync_ValidRequest_DispatcherReceivesRenderedContent()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");
        var mockDispatcher = LocalMockDispatcher.Success(ChannelType.Email);
        var sut = BuildService(dispatcher: mockDispatcher);

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Bob" },
            Recipient = new SendRecipient { Email = "bob@example.com" }
        };

        await sut.SendAsync(request);

        mockDispatcher.CallCount.Should().Be(1);
        mockDispatcher.SentRequests[0].Content.Should().Contain("Bob");
    }

    // ----------------------------------------------------------------
    // Template not found — NotFoundException
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_UnknownTemplateId_ThrowsNotFoundException()
    {
        var sut = BuildService();

        var request = new SendRequest
        {
            TemplateId = Guid.NewGuid(), // does not exist
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var act = async () => await sut.SendAsync(request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // Validation failures — ValidationException
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_DraftTemplate_ThrowsValidationException()
    {
        var template = await SeedDraftEmailTemplateAsync();
        var sut = BuildService();

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?>(),
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var act = async () => await sut.SendAsync(request);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*validation*");
    }

    [Fact]
    public async Task SendAsync_MissingPlaceholderData_ThrowsValidationException()
    {
        var template = await SeedPublishedEmailTemplateAsync("name", "city");
        var sut = BuildService();

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" }, // 'city' missing
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var act = async () => await sut.SendAsync(request);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task SendAsync_InvalidRecipientEmail_ThrowsValidationException()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");
        var sut = BuildService();

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { Email = "not-a-valid-email" }
        };

        var act = async () => await sut.SendAsync(request);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task SendAsync_ChannelMismatch_ThrowsValidationException()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");
        var sut = BuildService();

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Sms, // template is Email
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { PhoneNumber = "+33612345678" }
        };

        var act = async () => await sut.SendAsync(request);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----------------------------------------------------------------
    // Dispatch failure — returns failure response with tracking ID
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_DispatcherPermanentFailure_ReturnsFailureResponse()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");
        var failDispatcher = LocalMockDispatcher.PermanentFailure(
            ChannelType.Email, "SMTP rejected: invalid mailbox");
        var sut = BuildService(dispatcher: failDispatcher);

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var response = await sut.SendAsync(request);

        response.Success.Should().BeFalse();
        response.Status.Should().Be(SendStatus.Failed);
        response.ErrorDetail.Should().Contain("SMTP rejected");
        response.TrackingId.Should().NotBe(Guid.Empty);
    }

    // ----------------------------------------------------------------
    // No registered dispatcher — graceful failure
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_NoDispatcherForChannel_ReturnsFailureResponse()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");

        // Build service with an empty dispatcher registry (no Email dispatcher)
        var emptyRegistry = new ChannelDispatcherRegistry([]);
        var renderer = new ScribanTemplateRenderer(
            new Mock<Microsoft.Extensions.Logging.ILogger<ScribanTemplateRenderer>>().Object);
        var validator = new SendRequestValidator();
        var logService = CreateRealSendLogService();
        var logger = new Mock<IAppLogger<SingleSendService>>();

        var sut = new SingleSendService(
            new TemplateRepository(Context), renderer, emptyRegistry, validator, logService, logger.Object);

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var response = await sut.SendAsync(request);

        response.Success.Should().BeFalse();
        response.ErrorDetail.Should().Contain("No dispatcher registered");
    }

    // ----------------------------------------------------------------
    // Cancellation
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var template = await SeedPublishedEmailTemplateAsync("name");
        var sut = BuildService();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new SendRequest
        {
            TemplateId = template.Id,
            Channel = ChannelType.Email,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };

        var act = async () => await sut.SendAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
