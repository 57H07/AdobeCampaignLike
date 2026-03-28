using CampaignEngine.Application.Interfaces;

namespace CampaignEngine.Web.Middleware;

/// <summary>
/// Middleware that enforces per-API-key rate limiting.
/// Must run AFTER ApiKeyAuthenticationMiddleware so that the ApiKeyId context item
/// and ApiKeyRateLimitPerMinute context item are already populated.
///
/// Algorithm: Sliding 1-minute window via <see cref="IApiKeyRateLimiter"/>.
/// Limit stored per API key ID; window state is maintained across requests.
///
/// Behaviour:
///   - Requests without an API key (UI routes) are NOT rate-limited.
///   - All API-key-authenticated responses include X-RateLimit-* headers:
///       X-RateLimit-Limit     — the configured limit for this key (requests/minute).
///       X-RateLimit-Remaining — remaining requests in the current window.
///       X-RateLimit-Reset     — Unix timestamp (seconds) when the window resets.
///   - Requests exceeding the per-key limit return 429 Too Many Requests.
///   - Retry-After header is set to the seconds until the window resets.
///
/// US-033 business rules:
///   BR-1: Default 1000 req/min (from ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute).
///   BR-2: Sliding 1-minute window.
///   BR-3: Headers: X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset.
///   BR-4: 429 + Retry-After when limit exceeded.
/// </summary>
public class ApiKeyRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyRateLimitingMiddleware> _logger;

    public ApiKeyRateLimitingMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyRateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only rate-limit authenticated API key requests.
        // If no ApiKeyId was set by the auth middleware, this is a UI/cookie request.
        if (!context.Items.TryGetValue("ApiKeyId", out var apiKeyIdObj) || apiKeyIdObj is not Guid apiKeyId)
        {
            await _next(context);
            return;
        }

        // Retrieve the effective rate limit for this key (set by ApiKeyAuthenticationMiddleware).
        var rateLimitPerMinute = context.Items.TryGetValue("ApiKeyRateLimitPerMinute", out var limitObj)
                                  && limitObj is int limit
            ? limit
            : ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute;

        // Resolve rate limiter from DI (singleton — safe across requests).
        var rateLimiter = context.RequestServices.GetRequiredService<IApiKeyRateLimiter>();

        var result = await rateLimiter.TryAcquireAsync(apiKeyId, rateLimitPerMinute, context.RequestAborted);

        // Always add X-RateLimit headers (BR-3), even on rejected requests.
        var resetUnixSeconds = new DateTimeOffset(result.ResetAt).ToUnixTimeSeconds();
        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = resetUnixSeconds.ToString();

        if (result.IsAllowed)
        {
            await _next(context);
            return;
        }

        // Rate limit exceeded — return 429 (BR-4).
        _logger.LogWarning(
            "Rate limit exceeded for API key {ApiKeyId}. Limit={LimitPerMinute}/min Path={Path}",
            apiKeyId, rateLimitPerMinute, context.Request.Path);

        var retryAfterSeconds = result.RetryAfterSeconds;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

        await context.Response.WriteAsJsonAsync(new
        {
            status = 429,
            title = "Too Many Requests",
            detail = $"Rate limit exceeded. Maximum {rateLimitPerMinute} requests per minute per API key.",
            retryAfterSeconds
        });
    }
}
