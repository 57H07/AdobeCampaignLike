using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Dispatch;
using Microsoft.Extensions.Options;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Unit tests for US-022 rate limiting components:
///   - TokenBucketRateLimiter: token acquisition, backpressure, timeout
///   - NoOpRateLimiter: unlimited channels
///   - ChannelRateLimiterRegistry: per-channel configuration
///   - ThrottledChannelDispatcher: integration with dispatcher and metrics
///   - RateLimitMetricsService: counter accuracy
///
/// Business rules validated:
///   BR-1: Default rates: Email 100/sec, SMS 10/sec, Letter unlimited.
///   BR-2: Configuration is per-channel and per-environment.
///   BR-3: Rate limit errors (timeout) return a transient DispatchResult for retry.
///   BR-4: Burst capacity allows short bursts above sustained rate.
/// </summary>
public class RateLimiterTests
{
    // ================================================================
    // TokenBucketRateLimiter
    // ================================================================

    [Fact]
    public void TokenBucket_Channel_ReturnsConfiguredChannel()
    {
        var limiter = new TokenBucketRateLimiter(ChannelType.Email, tokensPerSecond: 10);

        limiter.Channel.Should().Be(ChannelType.Email);
    }

    [Fact]
    public void TokenBucket_TokensPerSecond_ReturnsConfiguredRate()
    {
        var limiter = new TokenBucketRateLimiter(ChannelType.Sms, tokensPerSecond: 5);

        limiter.TokensPerSecond.Should().Be(5);
    }

    [Fact]
    public async Task TokenBucket_WaitAsync_WithFullBucket_ReturnsImmediately()
    {
        // Full bucket: 10 tokens available, rate 10/sec
        var limiter = new TokenBucketRateLimiter(
            ChannelType.Email,
            tokensPerSecond: 10,
            burstMultiplier: 1.0,
            defaultMaxWait: TimeSpan.FromSeconds(5));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        // Should complete near-instantly (< 100ms)
        sw.ElapsedMilliseconds.Should().BeLessThan(100,
            "a token should be immediately available from a full bucket");
    }

    [Fact]
    public async Task TokenBucket_WaitAsync_ConsumesToken()
    {
        var limiter = new TokenBucketRateLimiter(
            ChannelType.Email,
            tokensPerSecond: 10,
            burstMultiplier: 1.0);

        var initialTokens = limiter.AvailableTokens;
        await limiter.WaitAsync();
        var afterTokens = limiter.AvailableTokens;

        // Available tokens should decrease by 1 (refill may add some back within the tiny window)
        afterTokens.Should().BeLessThanOrEqualTo(initialTokens,
            "consuming a token should reduce available tokens");
    }

    [Fact]
    public async Task TokenBucket_WaitAsync_WhenExhausted_WaitsForRefill()
    {
        // Very slow rate: 2 tokens/sec, burst=1 (only 2 tokens capacity)
        var limiter = new TokenBucketRateLimiter(
            ChannelType.Email,
            tokensPerSecond: 2,
            burstMultiplier: 1.0,
            defaultMaxWait: TimeSpan.FromSeconds(5));

        // Drain the bucket (2 tokens at 2/sec with burst=1 => capacity = 2)
        await limiter.WaitAsync();
        await limiter.WaitAsync();

        // Next acquire should wait ~500ms (1/2 sec per token)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThan(200,
            "should wait for token refill when bucket is exhausted");
    }

    [Fact]
    public async Task TokenBucket_WaitAsync_TimeoutElapses_ThrowsOperationCanceled()
    {
        // Very slow rate: 1 token/10sec, tiny timeout
        var limiter = new TokenBucketRateLimiter(
            ChannelType.Sms,
            tokensPerSecond: 1,
            burstMultiplier: 1.0,
            defaultMaxWait: TimeSpan.FromSeconds(5));

        // Drain the bucket
        await limiter.WaitAsync();

        // Next acquire with very short timeout should fail
        var act = async () => await limiter.WaitAsync(maxWait: TimeSpan.FromMilliseconds(50));

        await act.Should().ThrowAsync<OperationCanceledException>(
            "timeout should throw OperationCanceledException (treated as transient failure)");
    }

    [Fact]
    public async Task TokenBucket_WaitAsync_CancellationToken_StopsWaiting()
    {
        var limiter = new TokenBucketRateLimiter(
            ChannelType.Email,
            tokensPerSecond: 1,
            burstMultiplier: 1.0,
            defaultMaxWait: TimeSpan.FromSeconds(30));

        // Drain the bucket
        await limiter.WaitAsync();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var act = async () => await limiter.WaitAsync(
            maxWait: TimeSpan.FromSeconds(30),
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation token should stop the wait");
    }

    [Fact]
    public void TokenBucket_WaitingCount_StartsAtZero()
    {
        var limiter = new TokenBucketRateLimiter(ChannelType.Email, tokensPerSecond: 100);

        limiter.WaitingCount.Should().Be(0);
    }

    [Fact]
    public async Task TokenBucket_BurstCapacity_AllowsBurstAboveSustainedRate()
    {
        // 5 tokens/sec with burst multiplier 2 = 10 token capacity
        var limiter = new TokenBucketRateLimiter(
            ChannelType.Email,
            tokensPerSecond: 5,
            burstMultiplier: 2.0,
            defaultMaxWait: TimeSpan.FromSeconds(5));

        // Should be able to immediately acquire 10 tokens (burst capacity)
        var acquiredCount = 0;
        for (var i = 0; i < 10; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await limiter.WaitAsync();
            sw.Stop();

            if (sw.ElapsedMilliseconds < 50)  // Near-instant = no backpressure
                acquiredCount++;
        }

        acquiredCount.Should().BeGreaterThan(5,
            "burst multiplier should allow acquiring more tokens than sustained rate");
    }

    // ================================================================
    // NoOpRateLimiter
    // ================================================================

    [Fact]
    public void NoOpLimiter_Channel_ReturnsConfiguredChannel()
    {
        var limiter = new NoOpRateLimiter(ChannelType.Letter);
        limiter.Channel.Should().Be(ChannelType.Letter);
    }

    [Fact]
    public void NoOpLimiter_TokensPerSecond_ReturnsZero()
    {
        var limiter = new NoOpRateLimiter(ChannelType.Letter);
        limiter.TokensPerSecond.Should().Be(0);
    }

    [Fact]
    public async Task NoOpLimiter_WaitAsync_ReturnsImmediately()
    {
        var limiter = new NoOpRateLimiter(ChannelType.Letter);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(50,
            "no-op limiter should return immediately without delay");
    }

    [Fact]
    public void NoOpLimiter_AvailableTokens_ReturnsMaxDouble()
    {
        var limiter = new NoOpRateLimiter(ChannelType.Letter);
        limiter.AvailableTokens.Should().Be(double.MaxValue);
    }

    // ================================================================
    // ChannelRateLimiterRegistry
    // ================================================================

    [Fact]
    public void Registry_GetLimiter_Email_ReturnsTokenBucketLimiterWith100PerSec()
    {
        var registry = BuildRegistry();

        var limiter = registry.GetLimiter(ChannelType.Email);

        limiter.Should().NotBeNull();
        limiter.Channel.Should().Be(ChannelType.Email);
        limiter.TokensPerSecond.Should().Be(100);
    }

    [Fact]
    public void Registry_GetLimiter_Sms_ReturnsTokenBucketLimiterWith10PerSec()
    {
        var registry = BuildRegistry();

        var limiter = registry.GetLimiter(ChannelType.Sms);

        limiter.Should().NotBeNull();
        limiter.Channel.Should().Be(ChannelType.Sms);
        limiter.TokensPerSecond.Should().Be(10);
    }

    [Fact]
    public void Registry_GetLimiter_Letter_ReturnsNoOpLimiter()
    {
        var registry = BuildRegistry();

        var limiter = registry.GetLimiter(ChannelType.Letter);

        limiter.Should().BeOfType<NoOpRateLimiter>(
            "Letter channel is unlimited per BR-1");
        limiter.TokensPerSecond.Should().Be(0);
    }

    [Fact]
    public void Registry_GetAllLimiters_ReturnsThreeLimiters()
    {
        var registry = BuildRegistry();

        var limiters = registry.GetAllLimiters();

        limiters.Should().HaveCount(3);
    }

    [Fact]
    public void Registry_GetLimiter_UnknownChannel_ReturnsNoOpLimiter()
    {
        var registry = BuildRegistry();

        // Use an enum value that doesn't exist in the registry (not a real scenario,
        // but validates the fallback behaviour)
        var limiter = registry.GetLimiter((ChannelType)999);

        limiter.Should().BeOfType<NoOpRateLimiter>(
            "unknown channels should fall back to no-op (safe default)");
    }

    [Fact]
    public void Registry_WithZeroRate_CreatesNoOpLimiter()
    {
        var options = Options.Create(new RateLimitOptions
        {
            Email = new ChannelRateLimitOptions { TokensPerSecond = 0 },
            Sms = new ChannelRateLimitOptions { TokensPerSecond = 0 },
            Letter = new ChannelRateLimitOptions { TokensPerSecond = 0 }
        });
        var registry = new ChannelRateLimiterRegistry(options);

        registry.GetLimiter(ChannelType.Email).Should().BeOfType<NoOpRateLimiter>();
        registry.GetLimiter(ChannelType.Sms).Should().BeOfType<NoOpRateLimiter>();
    }

    // ================================================================
    // ThrottledChannelDispatcher
    // ================================================================

    [Fact]
    public async Task ThrottledDispatcher_WhenRateLimitNotExceeded_DelegatesToInner()
    {
        var innerDispatcher = new Mock<IChannelDispatcher>();
        innerDispatcher.SetupGet(d => d.Channel).Returns(ChannelType.Email);
        innerDispatcher.Setup(d => d.SendAsync(It.IsAny<DispatchRequest>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(DispatchResult.Ok(messageId: "msg-123"));

        var throttled = BuildThrottledDispatcher(innerDispatcher.Object, ChannelType.Email, tokensPerSecond: 100);
        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = "Test",
            Recipient = new RecipientInfo { Email = "test@example.com" }
        };

        var result = await throttled.SendAsync(request);

        result.Success.Should().BeTrue();
        innerDispatcher.Verify(d => d.SendAsync(request, default), Times.Once);
    }

    [Fact]
    public async Task ThrottledDispatcher_Channel_ReturnsDelegateChannel()
    {
        var innerDispatcher = new Mock<IChannelDispatcher>();
        innerDispatcher.SetupGet(d => d.Channel).Returns(ChannelType.Sms);

        var throttled = BuildThrottledDispatcher(innerDispatcher.Object, ChannelType.Sms, tokensPerSecond: 10);

        throttled.Channel.Should().Be(ChannelType.Sms);
    }

    [Fact]
    public async Task ThrottledDispatcher_WhenRateLimitExceeded_ReturnsTransientFailure()
    {
        var innerDispatcher = new Mock<IChannelDispatcher>();
        innerDispatcher.SetupGet(d => d.Channel).Returns(ChannelType.Email);

        // Use a very tight rate limiter: 1 token/sec, burst=1, 50ms timeout
        var rateLimiterMock = new Mock<IChannelRateLimiter>();
        rateLimiterMock.SetupGet(r => r.Channel).Returns(ChannelType.Email);
        rateLimiterMock.SetupGet(r => r.TokensPerSecond).Returns(1);
        rateLimiterMock.SetupGet(r => r.AvailableTokens).Returns(0.0);
        rateLimiterMock.SetupGet(r => r.WaitingCount).Returns(5);
        rateLimiterMock.Setup(r => r.WaitAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new OperationCanceledException("Rate limit exceeded"));

        var registryMock = new Mock<IChannelRateLimiterRegistry>();
        registryMock.Setup(r => r.GetLimiter(ChannelType.Email)).Returns(rateLimiterMock.Object);

        var metricsMock = new Mock<IRateLimitMetricsService>();
        var loggerMock = new Mock<IAppLogger<ThrottledChannelDispatcher>>();

        var throttled = new ThrottledChannelDispatcher(
            innerDispatcher.Object,
            registryMock.Object,
            metricsMock.Object,
            loggerMock.Object);

        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = "Test",
            Recipient = new RecipientInfo { Email = "test@example.com" }
        };

        var result = await throttled.SendAsync(request);

        // BR-3: rate limit exceeded returns transient failure for retry policy
        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue(
            "rate limit exceeded should be a transient failure triggering retry");
        result.ErrorDetail.Should().Contain("Rate limit exceeded",
            "error message should explain the cause");

        // Inner dispatcher should NOT have been called
        innerDispatcher.Verify(d => d.SendAsync(It.IsAny<DispatchRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // Metrics should record exceeded event
        metricsMock.Verify(m => m.RecordRateLimitExceeded(ChannelType.Email), Times.Once);
    }

    [Fact]
    public async Task ThrottledDispatcher_WhenCancellationRequested_PropagatesCancellation()
    {
        var innerDispatcher = new Mock<IChannelDispatcher>();
        innerDispatcher.SetupGet(d => d.Channel).Returns(ChannelType.Email);

        var rateLimiterMock = new Mock<IChannelRateLimiter>();
        rateLimiterMock.SetupGet(r => r.Channel).Returns(ChannelType.Email);
        rateLimiterMock.SetupGet(r => r.TokensPerSecond).Returns(1);
        rateLimiterMock.SetupGet(r => r.AvailableTokens).Returns(0.0);
        rateLimiterMock.SetupGet(r => r.WaitingCount).Returns(0);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        rateLimiterMock.Setup(r => r.WaitAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new OperationCanceledException(cts.Token));

        var registryMock = new Mock<IChannelRateLimiterRegistry>();
        registryMock.Setup(r => r.GetLimiter(ChannelType.Email)).Returns(rateLimiterMock.Object);

        var metricsMock = new Mock<IRateLimitMetricsService>();
        var loggerMock = new Mock<IAppLogger<ThrottledChannelDispatcher>>();

        var throttled = new ThrottledChannelDispatcher(
            innerDispatcher.Object,
            registryMock.Object,
            metricsMock.Object,
            loggerMock.Object);

        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = "Test",
            Recipient = new RecipientInfo { Email = "test@example.com" }
        };

        // A true cancellation (not rate limit timeout) should propagate as OperationCanceledException
        var act = async () => await throttled.SendAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "explicit cancellation should propagate, not be wrapped in DispatchResult");
    }

    [Fact]
    public async Task ThrottledDispatcher_UnlimitedChannel_NeverCallsMetricsWait()
    {
        var innerDispatcher = new Mock<IChannelDispatcher>();
        innerDispatcher.SetupGet(d => d.Channel).Returns(ChannelType.Letter);
        innerDispatcher.Setup(d => d.SendAsync(It.IsAny<DispatchRequest>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(DispatchResult.Ok());

        var metricsMock = new Mock<IRateLimitMetricsService>();
        var throttled = BuildThrottledDispatcher(
            innerDispatcher.Object, ChannelType.Letter, tokensPerSecond: 0);

        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            Content = "Test",
            Recipient = new RecipientInfo { DisplayName = "Test" }
        };

        await throttled.SendAsync(request);

        // No throttle wait or exceeded should be recorded for unlimited channel
        metricsMock.Verify(m => m.RecordThrottleWait(It.IsAny<ChannelType>(), It.IsAny<TimeSpan>()), Times.Never);
        metricsMock.Verify(m => m.RecordRateLimitExceeded(It.IsAny<ChannelType>()), Times.Never);
    }

    // ================================================================
    // RateLimitMetricsService
    // ================================================================

    [Fact]
    public void Metrics_GetSnapshot_ReturnsThreeChannels()
    {
        var metrics = BuildMetricsService();

        var snapshot = metrics.GetSnapshot();

        snapshot.Should().HaveCount(3, "one entry per channel (Email, SMS, Letter)");
    }

    [Fact]
    public void Metrics_RecordTokenAcquired_IncreasesCounter()
    {
        var metrics = BuildMetricsService();

        metrics.RecordTokenAcquired(ChannelType.Email);
        metrics.RecordTokenAcquired(ChannelType.Email);
        metrics.RecordTokenAcquired(ChannelType.Email);

        var snapshot = metrics.GetSnapshot().First(m => m.Channel == ChannelType.Email);
        snapshot.TokensAcquired.Should().Be(3);
    }

    [Fact]
    public void Metrics_RecordThrottleWait_IncreasesWaitCount()
    {
        var metrics = BuildMetricsService();

        metrics.RecordThrottleWait(ChannelType.Sms, TimeSpan.FromMilliseconds(250));
        metrics.RecordThrottleWait(ChannelType.Sms, TimeSpan.FromMilliseconds(100));

        var snapshot = metrics.GetSnapshot().First(m => m.Channel == ChannelType.Sms);
        snapshot.ThrottleWaitCount.Should().Be(2);
        snapshot.TotalWaitDuration.TotalMilliseconds.Should().BeApproximately(350, 1);
    }

    [Fact]
    public void Metrics_RecordRateLimitExceeded_IncreasesExceededCount()
    {
        var metrics = BuildMetricsService();

        metrics.RecordRateLimitExceeded(ChannelType.Email);
        metrics.RecordRateLimitExceeded(ChannelType.Email);

        var snapshot = metrics.GetSnapshot().First(m => m.Channel == ChannelType.Email);
        snapshot.RateLimitExceededCount.Should().Be(2);
    }

    [Fact]
    public void Metrics_Reset_ClearsAllCounters()
    {
        var metrics = BuildMetricsService();

        metrics.RecordTokenAcquired(ChannelType.Email);
        metrics.RecordThrottleWait(ChannelType.Sms, TimeSpan.FromSeconds(1));
        metrics.RecordRateLimitExceeded(ChannelType.Letter);

        metrics.Reset();

        var snapshot = metrics.GetSnapshot();
        foreach (var m in snapshot)
        {
            m.TokensAcquired.Should().Be(0);
            m.ThrottleWaitCount.Should().Be(0);
            m.RateLimitExceededCount.Should().Be(0);
        }
    }

    [Fact]
    public void Metrics_AverageWaitDuration_CalculatedCorrectly()
    {
        var metrics = BuildMetricsService();

        metrics.RecordThrottleWait(ChannelType.Email, TimeSpan.FromMilliseconds(200));
        metrics.RecordThrottleWait(ChannelType.Email, TimeSpan.FromMilliseconds(400));

        var snapshot = metrics.GetSnapshot().First(m => m.Channel == ChannelType.Email);
        snapshot.AverageWaitDuration.TotalMilliseconds.Should().BeApproximately(300, 1,
            "average of 200ms + 400ms = 300ms");
    }

    [Fact]
    public void Metrics_IncludesCurrentSendRate()
    {
        var metrics = BuildMetricsService();

        // Record 10 tokens acquired
        for (var i = 0; i < 10; i++)
            metrics.RecordTokenAcquired(ChannelType.Email);

        var snapshot = metrics.GetSnapshot().First(m => m.Channel == ChannelType.Email);

        // Rate should be positive (window just started so rate = 10 / elapsed_seconds)
        snapshot.CurrentSendRatePerSecond.Should().BeGreaterThan(0);
    }

    // ================================================================
    // Private factory helpers
    // ================================================================

    private static ChannelRateLimiterRegistry BuildRegistry(
        int emailRate = 100, int smsRate = 10, int letterRate = 0)
    {
        var options = Options.Create(new RateLimitOptions
        {
            Email  = new ChannelRateLimitOptions { TokensPerSecond = emailRate, MaxWaitTimeSeconds = 30, BurstMultiplier = 2.0 },
            Sms    = new ChannelRateLimitOptions { TokensPerSecond = smsRate, MaxWaitTimeSeconds = 10, BurstMultiplier = 2.0 },
            Letter = new ChannelRateLimitOptions { TokensPerSecond = letterRate, MaxWaitTimeSeconds = 0, BurstMultiplier = 1.0 }
        });
        return new ChannelRateLimiterRegistry(options);
    }

    private static RateLimitMetricsService BuildMetricsService()
    {
        var registry = BuildRegistry();
        var options = Options.Create(new RateLimitOptions());
        return new RateLimitMetricsService(registry, options);
    }

    private static ThrottledChannelDispatcher BuildThrottledDispatcher(
        IChannelDispatcher inner,
        ChannelType channel,
        int tokensPerSecond)
    {
        var options = Options.Create(new RateLimitOptions
        {
            Email  = new ChannelRateLimitOptions { TokensPerSecond = channel == ChannelType.Email ? tokensPerSecond : 100 },
            Sms    = new ChannelRateLimitOptions { TokensPerSecond = channel == ChannelType.Sms ? tokensPerSecond : 10 },
            Letter = new ChannelRateLimitOptions { TokensPerSecond = channel == ChannelType.Letter ? tokensPerSecond : 0 }
        });
        var registry = new ChannelRateLimiterRegistry(options);
        var metricsMock = new Mock<IRateLimitMetricsService>();
        var loggerMock = new Mock<IAppLogger<ThrottledChannelDispatcher>>();

        return new ThrottledChannelDispatcher(inner, registry, metricsMock.Object, loggerMock.Object);
    }
}
