using CampaignEngine.Application.Interfaces;
using CampaignEngine.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Tests.ApiKeys;

/// <summary>
/// Unit tests for ApiKeyRateLimitingMiddleware (US-033).
/// Verifies:
///   - Requests without ApiKeyId pass through (not rate-limited).
///   - Allowed requests pass through with X-RateLimit-* headers set.
///   - Rejected requests return 429 with Retry-After and X-RateLimit-* headers.
///   - Default rate limit is used when ApiKeyRateLimitPerMinute item is absent.
/// </summary>
public class ApiKeyRateLimitingMiddlewareTests
{
    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static ApiKeyRateLimitingMiddleware CreateMiddleware(
        RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        var logger = new Mock<ILogger<ApiKeyRateLimitingMiddleware>>();
        return new ApiKeyRateLimitingMiddleware(next, logger.Object);
    }

    private static HttpContext CreateContext(
        Guid? apiKeyId = null,
        int? rateLimitPerMinute = null,
        IApiKeyRateLimiter? rateLimiter = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new System.IO.MemoryStream();

        if (apiKeyId.HasValue)
            context.Items["ApiKeyId"] = apiKeyId.Value;

        if (rateLimitPerMinute.HasValue)
            context.Items["ApiKeyRateLimitPerMinute"] = rateLimitPerMinute.Value;

        // Wire up the DI service provider
        var services = new Mock<IServiceProvider>();
        if (rateLimiter is not null)
        {
            services
                .Setup(sp => sp.GetService(typeof(IApiKeyRateLimiter)))
                .Returns(rateLimiter);
        }
        context.RequestServices = services.Object;

        return context;
    }

    private static IApiKeyRateLimiter BuildAllowedLimiter(int limit = 1000, int remaining = 999)
    {
        var limiter = new Mock<IApiKeyRateLimiter>();
        limiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult
            {
                IsAllowed = true,
                Limit = limit,
                Remaining = remaining,
                ResetAt = DateTime.UtcNow.AddSeconds(55)
            });
        return limiter.Object;
    }

    private static IApiKeyRateLimiter BuildRejectedLimiter(int limit = 10)
    {
        var limiter = new Mock<IApiKeyRateLimiter>();
        limiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult
            {
                IsAllowed = false,
                Limit = limit,
                Remaining = 0,
                ResetAt = DateTime.UtcNow.AddSeconds(45)
            });
        return limiter.Object;
    }

    // ================================================================
    // No ApiKeyId — pass through (UI requests)
    // ================================================================

    [Fact]
    public async Task NoApiKeyId_PassesThroughWithoutRateLimiting()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(apiKeyId: null);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
        context.Response.Headers.ContainsKey("X-RateLimit-Limit").Should().BeFalse();
    }

    // ================================================================
    // Allowed request — headers set, next called
    // ================================================================

    [Fact]
    public async Task AllowedRequest_CallsNextMiddleware()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 100,
            rateLimiter: BuildAllowedLimiter(limit: 100, remaining: 99));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task AllowedRequest_SetsXRateLimitLimitHeader()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 500,
            rateLimiter: BuildAllowedLimiter(limit: 500, remaining: 499));

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("500");
    }

    [Fact]
    public async Task AllowedRequest_SetsXRateLimitRemainingHeader()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 100,
            rateLimiter: BuildAllowedLimiter(limit: 100, remaining: 75));

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("75");
    }

    [Fact]
    public async Task AllowedRequest_SetsXRateLimitResetHeader()
    {
        var middleware = CreateMiddleware();
        var keyId = Guid.NewGuid();
        var resetAt = DateTime.UtcNow.AddSeconds(50);

        var limiter = new Mock<IApiKeyRateLimiter>();
        limiter
            .Setup(l => l.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult
            {
                IsAllowed = true,
                Limit = 1000,
                Remaining = 999,
                ResetAt = resetAt
            });

        var context = CreateContext(
            apiKeyId: keyId,
            rateLimitPerMinute: 1000,
            rateLimiter: limiter.Object);

        await middleware.InvokeAsync(context);

        var headerValue = context.Response.Headers["X-RateLimit-Reset"].ToString();
        headerValue.Should().NotBeNullOrEmpty();

        // Header should contain a valid Unix timestamp
        long.TryParse(headerValue, out var resetUnix).Should().BeTrue();
        resetUnix.Should().BeGreaterThan(0);

        // The Unix timestamp should correspond approximately to resetAt
        var expectedUnix = new DateTimeOffset(resetAt).ToUnixTimeSeconds();
        resetUnix.Should().BeCloseTo(expectedUnix, 2);
    }

    [Fact]
    public async Task AllowedRequest_Returns200StatusCode()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 100,
            rateLimiter: BuildAllowedLimiter());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    // ================================================================
    // Rejected request (rate limit exceeded) — 429 + headers
    // ================================================================

    [Fact]
    public async Task RejectedRequest_Returns429StatusCode()
    {
        var middleware = CreateMiddleware(next: _ => Task.CompletedTask);
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 10,
            rateLimiter: BuildRejectedLimiter(limit: 10));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task RejectedRequest_DoesNotCallNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 10,
            rateLimiter: BuildRejectedLimiter(limit: 10));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RejectedRequest_SetsRetryAfterHeader()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 10,
            rateLimiter: BuildRejectedLimiter(limit: 10));

        await middleware.InvokeAsync(context);

        var retryAfter = context.Response.Headers["Retry-After"].ToString();
        retryAfter.Should().NotBeNullOrEmpty();
        int.TryParse(retryAfter, out var seconds).Should().BeTrue();
        seconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RejectedRequest_SetsXRateLimitRemainingToZero()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 10,
            rateLimiter: BuildRejectedLimiter(limit: 10));

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("0");
    }

    [Fact]
    public async Task RejectedRequest_SetsXRateLimitLimitHeader()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: 10,
            rateLimiter: BuildRejectedLimiter(limit: 10));

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("10");
    }

    // ================================================================
    // Default rate limit (no ApiKeyRateLimitPerMinute in context items)
    // ================================================================

    [Fact]
    public async Task NoRateLimitContextItem_UsesDefaultLimit()
    {
        var capturedLimit = 0;
        var limiter = new Mock<IApiKeyRateLimiter>();
        limiter
            .Setup(l => l.TryAcquireAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, int, CancellationToken>((_, lim, _) => capturedLimit = lim)
            .ReturnsAsync(new RateLimitResult
            {
                IsAllowed = true,
                Limit = ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute,
                Remaining = ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute - 1,
                ResetAt = DateTime.UtcNow.AddSeconds(60)
            });

        var middleware = CreateMiddleware();
        var context = CreateContext(
            apiKeyId: Guid.NewGuid(),
            rateLimitPerMinute: null, // no override in context
            rateLimiter: limiter.Object);

        await middleware.InvokeAsync(context);

        capturedLimit.Should().Be(ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute);
    }
}
