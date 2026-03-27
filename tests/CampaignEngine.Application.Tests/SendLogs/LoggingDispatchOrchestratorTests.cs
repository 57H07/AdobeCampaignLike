using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Tests.Dispatch;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Dispatch;

namespace CampaignEngine.Application.Tests.SendLogs;

/// <summary>
/// Logging completeness tests for LoggingDispatchOrchestrator (TASK-034-07).
/// Verifies that every send attempt is recorded in SEND_LOG before and after dispatch,
/// and that the correct status transitions are applied based on dispatch outcome.
///
/// Business rules validated:
///   - All sends logged before dispatch attempt (Pending)
///   - Status updated to Sent on success
///   - Status updated to Retrying on transient failure
///   - Status updated to Failed on permanent failure
///   - Error details captured in all failure paths
/// </summary>
public class LoggingDispatchOrchestratorTests
{
    private readonly Mock<ISendLogService> _sendLogService;
    private readonly Mock<IAppLogger<LoggingDispatchOrchestrator>> _logger;
    private readonly Mock<IChannelDispatcherRegistry> _registry;
    private readonly Mock<IRetryPolicy> _retryPolicy;

    public LoggingDispatchOrchestratorTests()
    {
        _sendLogService = new Mock<ISendLogService>();
        _logger = new Mock<IAppLogger<LoggingDispatchOrchestrator>>();
        _registry = new Mock<IChannelDispatcherRegistry>();

        // Default: no-retry policy (executes operation once, no retries)
        _retryPolicy = new Mock<IRetryPolicy>();
        _retryPolicy.Setup(p => p.MaxAttempts).Returns(3);
        _retryPolicy.Setup(p => p.ShouldRetry(It.IsAny<DispatchResult>(), It.IsAny<int>())).Returns(false);
        _retryPolicy
            .Setup(p => p.ExecuteAsync(
                It.IsAny<Func<int, CancellationToken, Task<DispatchResult>>>(),
                It.IsAny<Func<DispatchResult, int, TimeSpan, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<int, CancellationToken, Task<DispatchResult>>,
                     Func<DispatchResult, int, TimeSpan, Task>?,
                     CancellationToken>(
                (operation, _, ct) => operation(0, ct));
    }

    // ----------------------------------------------------------------
    // Helper: build orchestrator under test
    // ----------------------------------------------------------------

    private LoggingDispatchOrchestrator BuildOrchestrator() =>
        new(_registry.Object, _sendLogService.Object, _retryPolicy.Object, _logger.Object);

    private static DispatchRequest BuildRequest(
        ChannelType channel = ChannelType.Email,
        string recipientEmail = "test@example.com",
        Guid? campaignId = null)
    {
        return new DispatchRequest
        {
            Channel = channel,
            Content = "<p>Test</p>",
            CampaignId = campaignId ?? Guid.NewGuid(),
            Recipient = new RecipientInfo
            {
                Email = recipientEmail,
                ExternalRef = "ext-001"
            }
        };
    }

    // ----------------------------------------------------------------
    // Business rule: all sends logged before dispatch (Pending)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_AlwaysLogsPendingBeforeDispatch()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();
        var mockDispatcher = MockChannelDispatcher.Success(ChannelType.Email);

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher);

        var orchestrator = BuildOrchestrator();

        // Act
        await orchestrator.SendWithLoggingAsync(request);

        // Assert: LogPendingAsync called exactly once before dispatch
        _sendLogService.Verify(
            s => s.LogPendingAsync(
                request.CampaignId!.Value,
                request.CampaignStepId,
                ChannelType.Email,
                "test@example.com",
                "ext-001",
                null,
                default),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Business rule: status updated to Sent on success
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_SuccessfulDispatch_LogsSent()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();
        var mockDispatcher = MockChannelDispatcher.Success(ChannelType.Email);

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher);

        var orchestrator = BuildOrchestrator();

        // Act
        var (returnedId, result) = await orchestrator.SendWithLoggingAsync(request);

        // Assert
        returnedId.Should().Be(sendLogId);
        result.Success.Should().BeTrue();

        _sendLogService.Verify(
            s => s.LogSentAsync(sendLogId, It.IsAny<DateTime>(), default),
            Times.Once);

        // Failed/Retrying should NOT be called
        _sendLogService.Verify(
            s => s.LogFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), default),
            Times.Never);
        _sendLogService.Verify(
            s => s.LogRetryingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), default),
            Times.Never);
    }

    // ----------------------------------------------------------------
    // Business rule: status updated to Failed when all retries exhausted (transient failure)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_TransientFailure_AllRetriesExhausted_LogsFailed()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();
        var mockDispatcher = MockChannelDispatcher.TransientFailure(ChannelType.Email, "SMTP timeout");

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher);

        // No-retry policy: retries exhausted immediately
        _retryPolicy.Setup(p => p.ShouldRetry(It.IsAny<DispatchResult>(), It.IsAny<int>())).Returns(false);

        var orchestrator = BuildOrchestrator();

        // Act
        var (returnedId, result) = await orchestrator.SendWithLoggingAsync(request, currentRetryCount: 1);

        // Assert
        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();

        // When all retries are exhausted, the send is marked Failed
        _sendLogService.Verify(
            s => s.LogFailedAsync(sendLogId, It.IsAny<string>(), 3, default),
            Times.Once);

        // Sent/Retrying should NOT be called on exhaustion
        _sendLogService.Verify(
            s => s.LogSentAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), default),
            Times.Never);
        _sendLogService.Verify(
            s => s.LogRetryingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), default),
            Times.Never);
    }

    // ----------------------------------------------------------------
    // Business rule: retry callback triggers LogRetrying with incremented count
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_TransientFailure_RetryCallback_LogsRetrying()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();
        var mockDispatcher = MockChannelDispatcher.TransientFailure(ChannelType.Email, "SMTP timeout");
        Func<DispatchResult, int, TimeSpan, Task>? capturedOnRetry = null;

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher);

        // Capture the onRetry callback and invoke it once to simulate a retry
        _retryPolicy
            .Setup(p => p.ExecuteAsync(
                It.IsAny<Func<int, CancellationToken, Task<DispatchResult>>>(),
                It.IsAny<Func<DispatchResult, int, TimeSpan, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<int, CancellationToken, Task<DispatchResult>>,
                     Func<DispatchResult, int, TimeSpan, Task>?,
                     CancellationToken>(
                async (operation, onRetry, ct) =>
                {
                    var result = await operation(0, ct);
                    if (onRetry is not null)
                        await onRetry(result, 1, TimeSpan.FromSeconds(30));
                    return result;
                });

        var orchestrator = BuildOrchestrator();

        // Act
        await orchestrator.SendWithLoggingAsync(request, currentRetryCount: 1);

        // Assert: onRetry callback invoked LogRetrying with the incremented count
        _sendLogService.Verify(
            s => s.LogRetryingAsync(sendLogId, "SMTP timeout", 2, default),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Business rule: status updated to Failed on permanent failure
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_PermanentFailure_LogsFailed()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();
        var mockDispatcher = MockChannelDispatcher.PermanentFailure(ChannelType.Email, "Invalid email");

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher);

        var orchestrator = BuildOrchestrator();

        // Act
        var (_, result) = await orchestrator.SendWithLoggingAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();

        _sendLogService.Verify(
            s => s.LogFailedAsync(sendLogId, "Invalid email", 0, default),
            Times.Once);

        _sendLogService.Verify(
            s => s.LogSentAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), default),
            Times.Never);
        _sendLogService.Verify(
            s => s.LogRetryingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), default),
            Times.Never);
    }

    // ----------------------------------------------------------------
    // Business rule: unhandled exceptions logged as transient failure
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_DispatcherThrows_TreatedAsTransientFailure()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();
        var mockDispatcher = new Mock<IChannelDispatcher>();
        mockDispatcher.Setup(d => d.Channel).Returns(ChannelType.Email);
        mockDispatcher
            .Setup(d => d.SendAsync(It.IsAny<DispatchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP server unreachable"));

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher.Object);

        var orchestrator = BuildOrchestrator();

        // Act
        var (_, result) = await orchestrator.SendWithLoggingAsync(request);

        // Assert: exception is caught, treated as transient failure
        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
        result.ErrorDetail.Should().Contain("SMTP server unreachable");

        // With no-retry policy, transient failure → LogFailed after exhaustion
        _sendLogService.Verify(
            s => s.LogFailedAsync(sendLogId, It.Is<string>(e => e.Contains("SMTP server unreachable")), 3, default),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Business rule: no dispatcher registered logs failed immediately
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_NoDispatcherRegistered_LogsFailed()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(false);

        var orchestrator = BuildOrchestrator();

        // Act
        var (_, result) = await orchestrator.SendWithLoggingAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("Email");

        _sendLogService.Verify(
            s => s.LogFailedAsync(sendLogId, It.IsAny<string>(), 0, default),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Correlation ID propagation
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_WithCorrelationId_PropagatesCorrelationIdToLog()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var correlationId = "corr-abc-123";
        var request = BuildRequest();
        var mockDispatcher = MockChannelDispatcher.Success(ChannelType.Email);

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher);

        var orchestrator = BuildOrchestrator();

        // Act
        await orchestrator.SendWithLoggingAsync(request, correlationId: correlationId);

        // Assert: correlation ID passed to LogPendingAsync
        _sendLogService.Verify(
            s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // SMS channel: uses phone number as recipient address
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendWithLogging_SmsChannel_UsesPhoneNumberAsRecipientAddress()
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var request = new DispatchRequest
        {
            Channel = ChannelType.Sms,
            Content = "Hello from CampaignEngine",
            CampaignId = campaignId,
            Recipient = new RecipientInfo
            {
                PhoneNumber = "+1234567890",
                ExternalRef = "ext-sms-1"
            }
        };
        var mockDispatcher = MockChannelDispatcher.Success(ChannelType.Sms);

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                "+1234567890", It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Sms)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Sms)).Returns(mockDispatcher);

        var orchestrator = BuildOrchestrator();

        // Act
        await orchestrator.SendWithLoggingAsync(request);

        // Assert: phone number used as recipient address
        _sendLogService.Verify(
            s => s.LogPendingAsync(
                campaignId,
                null,
                ChannelType.Sms,
                "+1234567890",
                "ext-sms-1",
                null,
                default),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Retry count increments correctly via onRetry callback
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task SendWithLogging_RetryCallback_IncrementsRetryCount(
        int currentRetry, int expectedRetryCount)
    {
        // Arrange
        var sendLogId = Guid.NewGuid();
        var request = BuildRequest();
        var mockDispatcher = MockChannelDispatcher.TransientFailure(ChannelType.Email, "Timeout");

        _sendLogService
            .Setup(s => s.LogPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ChannelType>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendLogId);

        _registry.Setup(r => r.HasDispatcher(ChannelType.Email)).Returns(true);
        _registry.Setup(r => r.GetDispatcher(ChannelType.Email)).Returns(mockDispatcher);

        // Simulate one retry invocation of the onRetry callback
        _retryPolicy
            .Setup(p => p.ExecuteAsync(
                It.IsAny<Func<int, CancellationToken, Task<DispatchResult>>>(),
                It.IsAny<Func<DispatchResult, int, TimeSpan, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<int, CancellationToken, Task<DispatchResult>>,
                     Func<DispatchResult, int, TimeSpan, Task>?,
                     CancellationToken>(
                async (operation, onRetry, ct) =>
                {
                    var result = await operation(0, ct);
                    // Simulate that retry policy decides to retry once
                    if (onRetry is not null)
                        await onRetry(result, 1, TimeSpan.FromSeconds(30));
                    return result;
                });

        var orchestrator = BuildOrchestrator();

        // Act
        await orchestrator.SendWithLoggingAsync(request, currentRetryCount: currentRetry);

        // Assert: LogRetrying called with currentRetry + 1 (passed through onRetry callback)
        _sendLogService.Verify(
            s => s.LogRetryingAsync(sendLogId, "Timeout", currentRetry + 1, default),
            Times.Once);
    }
}
