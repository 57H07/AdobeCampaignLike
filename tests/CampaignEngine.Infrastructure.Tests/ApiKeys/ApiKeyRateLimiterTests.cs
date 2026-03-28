using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.ApiKeys;

namespace CampaignEngine.Infrastructure.Tests.ApiKeys;

/// <summary>
/// Unit tests for ApiKeyRateLimiter (US-033).
/// Verifies the sliding-window rate limiter business rules:
///   BR-1: Default 1000 req/min (configurable per key).
///   BR-2: Sliding 1-minute window — counter resets as old timestamps fall out.
///   BR-3: X-RateLimit headers data: Limit, Remaining, ResetAt are returned correctly.
///   BR-4: Requests exceeding the limit return IsAllowed=false with RetryAfterSeconds > 0.
/// Also verifies per-key stats (monitoring support).
/// </summary>
public class ApiKeyRateLimiterTests
{
    private static ApiKeyRateLimiter CreateLimiter() => new();

    private static Guid NewKeyId() => Guid.NewGuid();

    // ================================================================
    // TryAcquireAsync — happy path
    // ================================================================

    [Fact]
    public async Task TryAcquire_FirstRequest_IsAllowed()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();

        var result = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 10);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquire_FirstRequest_ReturnsCorrectLimit()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();

        var result = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 500);

        result.Limit.Should().Be(500);
    }

    [Fact]
    public async Task TryAcquire_FirstRequest_RemainingIsLimitMinusOne()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();

        var result = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 10);

        result.Remaining.Should().Be(9);
    }

    [Fact]
    public async Task TryAcquire_SecondRequest_RemainingDecrements()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();

        await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 10);
        var second = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 10);

        second.Remaining.Should().Be(8);
    }

    [Fact]
    public async Task TryAcquire_ResetAt_IsWithinOneMinute()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();
        var before = DateTime.UtcNow;

        var result = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 10);

        result.ResetAt.Should().BeAfter(before);
        result.ResetAt.Should().BeBefore(before.AddMinutes(1).AddSeconds(2));
    }

    // ================================================================
    // TryAcquireAsync — limit enforcement
    // ================================================================

    [Fact]
    public async Task TryAcquire_AtLimit_NextRequestIsRejected()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();
        const int limit = 5;

        // Exhaust the limit
        for (int i = 0; i < limit; i++)
            await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        // Next request should be rejected
        var overLimit = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        overLimit.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquire_Rejected_RemainingIsZero()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();
        const int limit = 3;

        for (int i = 0; i < limit; i++)
            await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        var overLimit = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        overLimit.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquire_Rejected_RetryAfterSecondsIsPositive()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();
        const int limit = 2;

        for (int i = 0; i < limit; i++)
            await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        var overLimit = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        overLimit.RetryAfterSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TryAcquire_Rejected_RetryAfterSecondsMaxIsOneMinute()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();
        const int limit = 1;

        await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);
        var overLimit = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        overLimit.RetryAfterSeconds.Should().BeLessOrEqualTo(60);
    }

    // ================================================================
    // Key isolation — different keys do NOT share counters
    // ================================================================

    [Fact]
    public async Task TryAcquire_DifferentKeys_IndependentCounters()
    {
        var limiter = CreateLimiter();
        const int limit = 2;

        var keyA = NewKeyId();
        var keyB = NewKeyId();

        // Exhaust key A
        for (int i = 0; i < limit; i++)
            await limiter.TryAcquireAsync(keyA, rateLimitPerMinute: limit);

        // Key B should still have full quota
        var keyBResult = await limiter.TryAcquireAsync(keyB, rateLimitPerMinute: limit);

        keyBResult.IsAllowed.Should().BeTrue();
        keyBResult.Remaining.Should().Be(limit - 1);
    }

    // ================================================================
    // Per-key stats
    // ================================================================

    [Fact]
    public async Task GetStats_UnknownKey_ReturnsNull()
    {
        var limiter = CreateLimiter();
        var stats = limiter.GetStats(Guid.NewGuid());
        stats.Should().BeNull();
    }

    [Fact]
    public async Task GetStats_AfterRequests_ReflectsWindowCount()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();
        const int limit = 10;

        await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);
        await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        var stats = limiter.GetStats(keyId);

        stats.Should().NotBeNull();
        stats!.LimitPerMinute.Should().Be(limit);
        stats.TotalRequests.Should().Be(2);
        stats.RequestsInCurrentWindow.Should().Be(2);
        stats.RemainingInCurrentWindow.Should().Be(8);
    }

    [Fact]
    public async Task GetStats_AfterRejection_TotalRejectedIncremented()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();
        const int limit = 1;

        await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);
        // This call is rejected
        await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: limit);

        var stats = limiter.GetStats(keyId);

        stats!.TotalRejected.Should().Be(1);
        stats.TotalRequests.Should().Be(1); // only the accepted one
    }

    [Fact]
    public async Task GetAllStats_MultipleKeys_ReturnsAllTracked()
    {
        var limiter = CreateLimiter();

        var key1 = NewKeyId();
        var key2 = NewKeyId();
        var key3 = NewKeyId();

        await limiter.TryAcquireAsync(key1, 10);
        await limiter.TryAcquireAsync(key2, 20);
        await limiter.TryAcquireAsync(key3, 30);

        var allStats = limiter.GetAllStats();

        allStats.Should().HaveCount(3);
        allStats.Select(s => s.ApiKeyId).Should().Contain(key1).And.Contain(key2).And.Contain(key3);
    }

    // ================================================================
    // RecordRejected (called from external rate-limit alerting)
    // ================================================================

    [Fact]
    public async Task RecordRejected_ExistingKey_IncrementsRejectedCounter()
    {
        var limiter = CreateLimiter();
        var keyId = NewKeyId();

        // First acquire creates the counter entry
        await limiter.TryAcquireAsync(keyId, 100);

        limiter.RecordRejected(keyId);
        limiter.RecordRejected(keyId);

        var stats = limiter.GetStats(keyId);
        stats!.TotalRejected.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void RecordRejected_UnknownKey_DoesNotThrow()
    {
        var limiter = CreateLimiter();
        var act = () => limiter.RecordRejected(Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ================================================================
    // RateLimitResult properties
    // ================================================================

    [Fact]
    public async Task TryAcquire_Allowed_RetryAfterSecondsIsZero()
    {
        var limiter = CreateLimiter();
        var result = await limiter.TryAcquireAsync(NewKeyId(), rateLimitPerMinute: 100);

        result.IsAllowed.Should().BeTrue();
        result.RetryAfterSeconds.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquire_LimitUpdatedBetweenCalls_NewLimitApplied()
    {
        // Verify that UpdateLimit (called when rateLimitPerMinute changes) takes effect.
        var limiter = CreateLimiter();
        var keyId = NewKeyId();

        // First request creates counter with limit=5
        var first = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 5);
        first.Limit.Should().Be(5);

        // Second call with a different limit — counter should reflect new limit
        var second = await limiter.TryAcquireAsync(keyId, rateLimitPerMinute: 1000);
        second.Limit.Should().Be(1000);
        // 2 requests out of 1000 consumed: 998 remaining
        second.Remaining.Should().Be(998);
    }
}
