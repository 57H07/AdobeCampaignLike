using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Dispatch;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Application.Tests.SendLogs;

/// <summary>
/// Unit tests for RetryPolicy — retry logic and exponential backoff timing.
///
/// TASK-035-05: Retry logic tests.
/// TASK-035-06: Exponential backoff timing tests.
///
/// Business rules validated:
///   BR-1: Max 3 retry attempts per send.
///   BR-2: Exponential backoff delays: 30s, 2min (120s), 10min (600s).
///   BR-3: Only transient failures are retried; permanent failures are not.
///   BR-4: Retry count tracked after each attempt.
/// </summary>
public class RetryPolicyTests
{
    // ----------------------------------------------------------------
    // Helper: build a RetryPolicy with default config (3 attempts, 30/120/600s)
    // ----------------------------------------------------------------

    private static RetryPolicy BuildPolicy(int maxAttempts = 3, int[]? delays = null)
    {
        delays ??= [30, 120, 600];
        var options = Options.Create(new CampaignEngineOptions
        {
            BatchProcessing = new BatchProcessingOptions
            {
                MaxRetryAttempts = maxAttempts,
                RetryDelaysSeconds = delays
            }
        });
        return new RetryPolicy(options);
    }

    // ----------------------------------------------------------------
    // MaxAttempts property
    // ----------------------------------------------------------------

    [Fact]
    public void MaxAttempts_DefaultConfig_ReturnsThree()
    {
        var policy = BuildPolicy();

        policy.MaxAttempts.Should().Be(3);
    }

    [Fact]
    public void MaxAttempts_CustomConfig_ReturnsConfiguredValue()
    {
        var policy = BuildPolicy(maxAttempts: 5);

        policy.MaxAttempts.Should().Be(5);
    }

    // ----------------------------------------------------------------
    // TASK-035-06: Exponential backoff delay values
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(1, 30)]    // First retry: 30 seconds
    [InlineData(2, 120)]   // Second retry: 2 minutes
    [InlineData(3, 600)]   // Third retry: 10 minutes
    public void GetDelay_BusinessRuleBackoffValues_MatchSpecification(int attemptNumber, int expectedSeconds)
    {
        var policy = BuildPolicy();

        var delay = policy.GetDelay(attemptNumber);

        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds),
            because: $"attempt {attemptNumber} should use {expectedSeconds}s backoff per US-035 BR-2");
    }

    [Fact]
    public void GetDelay_AttemptBeyondDelayArrayBound_ReturnLastDelay()
    {
        // When attempt number exceeds the delay array, clamp to the last value
        var policy = BuildPolicy(maxAttempts: 5, delays: [30, 120, 600]);

        var delay = policy.GetDelay(4);

        delay.Should().Be(TimeSpan.FromSeconds(600),
            because: "exceeding the delay array should clamp to the last configured delay");
    }

    [Fact]
    public void GetDelay_AttemptZero_ReturnFirstDelay()
    {
        // Edge case: attemptNumber = 0 should map to index -1 → clamped to 0
        var policy = BuildPolicy();

        var delay = policy.GetDelay(0);

        delay.Should().Be(TimeSpan.FromSeconds(30),
            because: "attempt 0 maps to index 0 in the delay array");
    }

    // ----------------------------------------------------------------
    // ShouldRetry: success never retried
    // ----------------------------------------------------------------

    [Fact]
    public void ShouldRetry_SuccessResult_ReturnsFalse()
    {
        var policy = BuildPolicy();
        var successResult = DispatchResult.Ok("msg-123");

        var shouldRetry = policy.ShouldRetry(successResult, currentRetryCount: 0);

        shouldRetry.Should().BeFalse(because: "successful dispatches are never retried");
    }

    // ----------------------------------------------------------------
    // ShouldRetry: permanent failures never retried (BR-3)
    // ----------------------------------------------------------------

    [Fact]
    public void ShouldRetry_PermanentFailure_ReturnsFalse()
    {
        var policy = BuildPolicy();
        var permanentFailure = DispatchResult.Fail("Invalid email address", isTransient: false);

        var shouldRetry = policy.ShouldRetry(permanentFailure, currentRetryCount: 0);

        shouldRetry.Should().BeFalse(because: "permanent failures must not be retried per BR-3");
    }

    // ----------------------------------------------------------------
    // ShouldRetry: transient failures retried until max attempts (BR-1, BR-3)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(0)]  // 0 retries so far — can retry
    [InlineData(1)]  // 1 retry so far — can retry
    [InlineData(2)]  // 2 retries so far — can retry
    public void ShouldRetry_TransientFailure_BelowMaxAttempts_ReturnsTrue(int currentRetryCount)
    {
        var policy = BuildPolicy(maxAttempts: 3);
        var transientFailure = DispatchResult.Fail("SMTP timeout", isTransient: true);

        var shouldRetry = policy.ShouldRetry(transientFailure, currentRetryCount);

        shouldRetry.Should().BeTrue(
            because: $"transient failure with {currentRetryCount} retries is below max 3 attempts");
    }

    [Fact]
    public void ShouldRetry_TransientFailure_AtMaxAttempts_ReturnsFalse()
    {
        var policy = BuildPolicy(maxAttempts: 3);
        var transientFailure = DispatchResult.Fail("SMTP timeout", isTransient: true);

        // currentRetryCount == maxAttempts means all retries are exhausted
        var shouldRetry = policy.ShouldRetry(transientFailure, currentRetryCount: 3);

        shouldRetry.Should().BeFalse(
            because: "retry count at max attempts means all retries are exhausted per BR-1");
    }

    // ----------------------------------------------------------------
    // TASK-035-05: ExecuteAsync — success on first attempt
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SuccessOnFirstAttempt_CallsOperationOnce()
    {
        var policy = BuildPolicy();
        var callCount = 0;

        var result = await policy.ExecuteAsync(
            operation: (retryAttempt, ct) =>
            {
                callCount++;
                return Task.FromResult(DispatchResult.Ok("msg-001"));
            });

        result.Success.Should().BeTrue();
        callCount.Should().Be(1, because: "a successful first attempt requires no retries");
    }

    // ----------------------------------------------------------------
    // ExecuteAsync — permanent failure is not retried (BR-3)
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PermanentFailure_CallsOperationOnceAndReturns()
    {
        var policy = BuildPolicy();
        var callCount = 0;

        var result = await policy.ExecuteAsync(
            operation: (_, ct) =>
            {
                callCount++;
                return Task.FromResult(DispatchResult.Fail("Invalid address", isTransient: false));
            });

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        callCount.Should().Be(1, because: "permanent failures are never retried");
    }

    // ----------------------------------------------------------------
    // ExecuteAsync — transient failure retried up to maxAttempts (BR-1)
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TransientFailureAllRetries_OperationCalledMaxPlusOneTimes()
    {
        // Policy with 3 max attempts and zero delays for test speed
        var policy = BuildPolicy(maxAttempts: 3, delays: [0, 0, 0]);
        var callCount = 0;

        var result = await policy.ExecuteAsync(
            operation: (_, ct) =>
            {
                callCount++;
                return Task.FromResult(DispatchResult.Fail("SMTP timeout", isTransient: true));
            });

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
        // 1 initial attempt + 3 retries = 4 calls total
        callCount.Should().Be(4,
            because: "3 retry attempts = 1 original + 3 retries per BR-1");
    }

    // ----------------------------------------------------------------
    // ExecuteAsync — succeeds on retry (transient then success)
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TransientFailureThenSuccess_ReturnsSuccess()
    {
        var policy = BuildPolicy(maxAttempts: 3, delays: [0, 0, 0]);
        var callCount = 0;

        var result = await policy.ExecuteAsync(
            operation: (_, ct) =>
            {
                callCount++;
                if (callCount < 3)
                    return Task.FromResult(DispatchResult.Fail("SMTP timeout", isTransient: true));
                return Task.FromResult(DispatchResult.Ok("msg-recovered"));
            });

        result.Success.Should().BeTrue("the operation succeeded on the 3rd attempt");
        result.MessageId.Should().Be("msg-recovered");
        callCount.Should().Be(3, "failed twice then succeeded on 3rd attempt");
    }

    // ----------------------------------------------------------------
    // ExecuteAsync — onRetry callback invoked before each retry
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TransientFailures_OnRetryCallbackInvokedForEachRetry()
    {
        var policy = BuildPolicy(maxAttempts: 3, delays: [0, 0, 0]);
        var onRetryInvocations = new List<(int RetryAttemptNumber, TimeSpan Delay)>();

        await policy.ExecuteAsync(
            operation: (_, ct) =>
                Task.FromResult(DispatchResult.Fail("SMTP timeout", isTransient: true)),
            onRetry: (result, retryAttemptNumber, delay) =>
            {
                onRetryInvocations.Add((retryAttemptNumber, delay));
                return Task.CompletedTask;
            });

        onRetryInvocations.Should().HaveCount(3,
            because: "onRetry is called before each of the 3 retry attempts");
        onRetryInvocations[0].RetryAttemptNumber.Should().Be(1);
        onRetryInvocations[1].RetryAttemptNumber.Should().Be(2);
        onRetryInvocations[2].RetryAttemptNumber.Should().Be(3);
    }

    // ----------------------------------------------------------------
    // TASK-035-06: onRetry receives correct delay values per backoff schedule
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OnRetryCallback_ReceivesCorrectBackoffDelays()
    {
        // Use real delays but capture them (Task.Delay with real values is tested via GetDelay)
        // Here we verify the delay argument passed to onRetry matches the schedule
        var policy = BuildPolicy(maxAttempts: 3, delays: [0, 0, 0]);
        var capturedDelays = new List<TimeSpan>();

        await policy.ExecuteAsync(
            operation: (_, ct) =>
                Task.FromResult(DispatchResult.Fail("timeout", isTransient: true)),
            onRetry: (_, retryAttemptNumber, delay) =>
            {
                capturedDelays.Add(delay);
                return Task.CompletedTask;
            });

        capturedDelays.Should().HaveCount(3);
        // With delays: [0,0,0], all delays should be 0s
        capturedDelays.Should().AllSatisfy(d => d.TotalSeconds.Should().Be(0));
    }

    [Fact]
    public async Task ExecuteAsync_OnRetryCallback_ReceivesBusinessRuleBackoffDelays()
    {
        // Use a policy with 2 retries and the real business rule delays to verify
        // delay argument matches GetDelay output without actually sleeping
        var policy = BuildPolicy(maxAttempts: 2, delays: [30, 120]);
        var capturedDelays = new List<TimeSpan>();

        // We can't wait 30s in a test — use a fast CancellationToken to cancel during delay
        using var cts = new CancellationTokenSource();

        try
        {
            await policy.ExecuteAsync(
                operation: (_, ct) =>
                    Task.FromResult(DispatchResult.Fail("timeout", isTransient: true)),
                onRetry: (_, retryAttemptNumber, delay) =>
                {
                    capturedDelays.Add(delay);
                    // Cancel after capturing both callbacks to avoid waiting for actual delays
                    if (retryAttemptNumber >= 2)
                        cts.Cancel();
                    return Task.CompletedTask;
                },
                cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected — we cancelled after capturing the delays
        }

        capturedDelays.Should().HaveCountGreaterOrEqualTo(1);
        capturedDelays[0].Should().Be(TimeSpan.FromSeconds(30),
            because: "first retry delay should be 30 seconds per US-035 BR-2");
    }

    // ----------------------------------------------------------------
    // ExecuteAsync — cancellation is respected
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var policy = BuildPolicy(maxAttempts: 3, delays: [0, 0, 0]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await policy.ExecuteAsync(
            operation: (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(DispatchResult.Ok());
            },
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ----------------------------------------------------------------
    // ExecuteAsync — retry attempt number passed correctly to operation
    // ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TransientFailures_OperationReceivesIncrementingRetryAttempt()
    {
        var policy = BuildPolicy(maxAttempts: 3, delays: [0, 0, 0]);
        var capturedAttempts = new List<int>();

        await policy.ExecuteAsync(
            operation: (retryAttempt, ct) =>
            {
                capturedAttempts.Add(retryAttempt);
                return Task.FromResult(DispatchResult.Fail("timeout", isTransient: true));
            });

        capturedAttempts.Should().BeEquivalentTo(
            new[] { 0, 1, 2, 3 },
            options => options.WithStrictOrdering(),
            because: "retry attempt number increments from 0 (first attempt) to 3 (final retry)");
    }
}
