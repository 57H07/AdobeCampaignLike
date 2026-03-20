using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Tests for SmsDispatcher and related phone number validation, truncation,
/// and provider client plumbing.
///
/// TASK-020-06: Integration tests with provider sandbox.
///
/// All tests use TestableSmsProviderClient to bypass real HTTP calls.
/// </summary>
public class SmsDispatcherTests
{
    private readonly SmsOptions _defaultOptions = new()
    {
        Provider = "Generic",
        ProviderApiUrl = "https://sms-provider.example.com/send",
        ApiKey = "test-api-key",
        DefaultSenderId = "+10000000000",
        MaxMessageLength = 160,
        IsEnabled = true,
        ValidatePhoneNumbers = true,
        TimeoutSeconds = 10
    };

    // ----------------------------------------------------------------
    // Channel property
    // ----------------------------------------------------------------

    [Fact]
    public void Channel_ReturnsSms()
    {
        var dispatcher = CreateDispatcher();
        dispatcher.Channel.Should().Be(ChannelType.Sms);
    }

    // ----------------------------------------------------------------
    // Happy path
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ValidRequest_ReturnsSuccess()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("+12025551234");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
        result.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ValidRequest_MessageIdFromProvider()
    {
        var dispatcher = CreateDispatcher(messageId: "MSG-ABC-123");
        var request = BuildRequest("+12025551234");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("MSG-ABC-123");
    }

    // ----------------------------------------------------------------
    // Channel disabled
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ChannelDisabled_ReturnsPermanentFailure()
    {
        var opts = new SmsOptions { IsEnabled = false };
        var dispatcher = CreateDispatcher(options: opts);
        var request = BuildRequest("+12025551234");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("disabled");
    }

    // ----------------------------------------------------------------
    // Phone number validation (BR-1, BR-4)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_NoPhoneNumber_ReturnsPermanentFailure()
    {
        var dispatcher = CreateDispatcher();
        var request = new DispatchRequest
        {
            Channel = ChannelType.Sms,
            Content = "Hello",
            Recipient = new RecipientInfo { PhoneNumber = null }
        };

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("phone number is required");
    }

    [Fact]
    public async Task SendAsync_EmptyPhoneNumber_ReturnsPermanentFailure()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest(string.Empty);

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Theory]
    [InlineData("12025551234")]       // Missing leading +
    [InlineData("+1")]                // Too short
    [InlineData("+(123)456-7890")]    // Contains non-digit chars after +
    [InlineData("+12345678901234567")]// Too long (>15 digits)
    [InlineData("not-a-phone")]       // Completely invalid
    public async Task SendAsync_InvalidE164Format_ReturnsPermanentFailure(string phoneNumber)
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest(phoneNumber);

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("E.164");
    }

    [Fact]
    public async Task SendAsync_ValidationDisabled_AcceptsNonE164Number()
    {
        // When ValidatePhoneNumbers is false, non-E164 numbers pass through to the provider
        var opts = new SmsOptions
        {
            IsEnabled = true,
            ValidatePhoneNumbers = false,  // disable validation
            MaxMessageLength = 160,
            ProviderApiUrl = "https://example.com",
            ApiKey = "key"
        };
        var dispatcher = CreateDispatcher(options: opts);
        var request = BuildRequest("0612345678");  // French local format — not E.164

        var result = await dispatcher.SendAsync(request);

        // Provider client (stub) succeeds
        result.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("+12025551234")]          // US
    [InlineData("+441234567890")]         // UK
    [InlineData("+33123456789")]          // France
    [InlineData("+819012345678")]         // Japan
    [InlineData("+27211234567")]          // South Africa
    public async Task SendAsync_ValidE164Numbers_Succeed(string phoneNumber)
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest(phoneNumber);

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // Message truncation (BR-2)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ShortMessage_NotTruncated()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("+12025551234", content: "Hello, World!");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void TruncateMessage_ShortText_ReturnsUnchanged()
    {
        var text = "Hello";
        SmsDispatcher.TruncateMessage(text, 160).Should().Be("Hello");
    }

    [Fact]
    public void TruncateMessage_ExactlyMaxLength_ReturnsUnchanged()
    {
        var text = new string('A', 160);
        SmsDispatcher.TruncateMessage(text, 160).Should().HaveLength(160);
    }

    [Fact]
    public void TruncateMessage_LongerThanMax_TruncatesAtWordBoundary()
    {
        // "Hello World" truncated at 7 chars — "Hello W" cuts mid-word, backs to "Hello"
        var text = "Hello World Extra Words";
        var result = SmsDispatcher.TruncateMessage(text, 7);
        result.Should().Be("Hello");
    }

    [Fact]
    public void TruncateMessage_LongerThanMax_CutExactlyOnSpaceBoundary()
    {
        var text = "Hello World Extra";
        // Cut at 11 chars = "Hello World" — next char is ' ', so no backup needed
        var result = SmsDispatcher.TruncateMessage(text, 11);
        result.Should().Be("Hello World");
    }

    [Fact]
    public void TruncateMessage_NoWordBoundaryWithinLimit_HardTruncates()
    {
        var text = "AAAAAABBBBBCCCCC";
        // No spaces — hard truncate at 6
        var result = SmsDispatcher.TruncateMessage(text, 6);
        result.Should().Be("AAAAAA");
    }

    [Fact]
    public void TruncateMessage_EmptyString_ReturnsEmpty()
    {
        SmsDispatcher.TruncateMessage(string.Empty, 160).Should().Be(string.Empty);
    }

    [Fact]
    public async Task SendAsync_MessageExceeding160_TruncatedBefore_Sending()
    {
        var longMessage = new string('A', 200);
        var opts = new SmsOptions
        {
            IsEnabled = true,
            ValidatePhoneNumbers = true,
            MaxMessageLength = 160,
            ProviderApiUrl = "https://example.com",
            ApiKey = "key"
        };
        var capturer = new CapturingProviderClient();
        var dispatcher = CreateDispatcher(capturer, opts);
        var request = BuildRequest("+12025551234", content: longMessage);

        await dispatcher.SendAsync(request);

        capturer.LastMessage.Should().HaveLength(160);
    }

    [Fact]
    public async Task SendAsync_CustomMaxLength_Respected()
    {
        var capturer = new CapturingProviderClient();
        var opts = new SmsOptions
        {
            IsEnabled = true,
            ValidatePhoneNumbers = true,
            MaxMessageLength = 80,
            ProviderApiUrl = "https://example.com",
            ApiKey = "key"
        };
        var dispatcher = CreateDispatcher(capturer, opts);
        var request = BuildRequest("+12025551234", content: new string('X', 100));

        await dispatcher.SendAsync(request);

        capturer.LastMessage.Should().HaveLength(80);
    }

    // ----------------------------------------------------------------
    // Provider error handling (transient vs permanent)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_TransientProviderFailure_ReturnsTransientResult()
    {
        var dispatcher = CreateDispatcherWithFailure(isTransient: true);
        var request = BuildRequest("+12025551234");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_PermanentProviderFailure_ReturnsPermanentResult()
    {
        var dispatcher = CreateDispatcherWithFailure(isTransient: false);
        var request = BuildRequest("+12025551234");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_CancellationRequested_ReturnsTransientFailure()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("+12025551234");

        // The stub provider client does not check cancellation, but we pass a cancelled token
        // to exercise the OperationCanceledException path via the dispatcher's cancellation guard.
        var cancellingClient = new CancellationThrowingProviderClient();
        var optsCancelling = new SmsOptions { IsEnabled = true, MaxMessageLength = 160, ValidatePhoneNumbers = true };
        var cancellingDispatcher = CreateDispatcher(cancellingClient, optsCancelling);

        var result = await cancellingDispatcher.SendAsync(request, cts.Token);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // Phone number validator unit tests (TASK-020-03)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("+12025551234", true)]
    [InlineData("+441234567890", true)]
    [InlineData("+33123456789", true)]
    [InlineData("+1234567", true)]           // Minimum (7 digits after country code)
    [InlineData("+123456789012345", true)]   // Maximum (15 digits)
    [InlineData("12025551234", false)]        // Missing +
    [InlineData("+", false)]                  // + only
    [InlineData("+1", false)]                 // Too short
    [InlineData("+1234567890123456", false)]  // Too long (16 digits)
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PhoneNumberValidator_IsValidE164(string? phone, bool expected)
    {
        PhoneNumberValidator.IsValidE164(phone).Should().Be(expected);
    }

    [Theory]
    [InlineData("+1 (202) 555-1234", "+12025551234")]
    [InlineData("+44 1234-567-890", "+441234567890")]
    [InlineData("+33 1.23.45.67.89", "+33123456789")]
    public void PhoneNumberValidator_Normalize_RemovesFormatting(string input, string expected)
    {
        PhoneNumberValidator.Normalize(input).Should().Be(expected);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private SmsDispatcher CreateDispatcher(
        SmsProviderClient? client = null,
        SmsOptions? options = null,
        string? messageId = null)
    {
        var opts = Options.Create(options ?? _defaultOptions);
        var providerClient = client ?? new TestableSmsProviderClient(
            exception: null,
            messageId: messageId);
        return new SmsDispatcher(opts, providerClient, NullLogger<SmsDispatcher>.Instance);
    }

    private SmsDispatcher CreateDispatcherWithFailure(bool isTransient)
    {
        var ex = new SmsDispatchException(
            isTransient ? "Simulated transient SMS failure" : "Simulated permanent SMS failure",
            isTransient: isTransient,
            httpStatusCode: isTransient ? 503 : 400);
        var client = new TestableSmsProviderClient(exception: ex, messageId: null);
        return CreateDispatcher(client);
    }

    private static DispatchRequest BuildRequest(string phoneNumber, string? content = null) => new()
    {
        Channel = ChannelType.Sms,
        Content = content ?? "Hello! This is a test SMS message from CampaignEngine.",
        Recipient = new RecipientInfo
        {
            PhoneNumber = phoneNumber,
            DisplayName = "Test Recipient"
        }
    };
}

// ----------------------------------------------------------------
// Test doubles
// ----------------------------------------------------------------

/// <summary>
/// Provider client that succeeds without making real HTTP calls.
/// Optionally throws a SmsDispatchException to simulate provider failures.
/// </summary>
internal class TestableSmsProviderClient : SmsProviderClient
{
    private readonly Exception? _exception;
    private readonly string? _messageId;

    public TestableSmsProviderClient(Exception? exception, string? messageId)
        : base(
            new SmsOptions { ProviderApiUrl = "https://stub", ApiKey = "stub" },
            new Mock<IHttpClientFactory>().Object,
            NullLogger<SmsProviderClient>.Instance)
    {
        _exception = exception;
        _messageId = messageId;
    }

    public override Task<SmsProviderResult> SendAsync(
        string toPhoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (_exception is not null)
            throw _exception;

        return Task.FromResult(new SmsProviderResult(_messageId, 200, null));
    }
}

/// <summary>
/// Provider client that captures the last sent message for assertion.
/// </summary>
internal class CapturingProviderClient : SmsProviderClient
{
    public string? LastMessage { get; private set; }
    public string? LastPhoneNumber { get; private set; }

    public CapturingProviderClient()
        : base(
            new SmsOptions { ProviderApiUrl = "https://stub", ApiKey = "stub" },
            new Mock<IHttpClientFactory>().Object,
            NullLogger<SmsProviderClient>.Instance)
    { }

    public override Task<SmsProviderResult> SendAsync(
        string toPhoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        LastPhoneNumber = toPhoneNumber;
        LastMessage = message;
        return Task.FromResult(new SmsProviderResult("MSG-TEST", 200, null));
    }
}

/// <summary>
/// Provider client that throws OperationCanceledException when called.
/// Used to verify cancellation token propagation.
/// </summary>
internal class CancellationThrowingProviderClient : SmsProviderClient
{
    public CancellationThrowingProviderClient()
        : base(
            new SmsOptions { ProviderApiUrl = "https://stub", ApiKey = "stub" },
            new Mock<IHttpClientFactory>().Object,
            NullLogger<SmsProviderClient>.Instance)
    { }

    public override Task<SmsProviderResult> SendAsync(
        string toPhoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        throw new OperationCanceledException("Simulated cancellation.");
    }
}
