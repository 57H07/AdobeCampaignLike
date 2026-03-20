using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace CampaignEngine.Web.Middleware;

/// <summary>
/// Middleware that enforces per-API-key rate limiting.
/// Must run AFTER ApiKeyAuthenticationMiddleware so that the ApiKeyId context item
/// and ApiKeyRateLimitPerMinute context item are already populated.
///
/// Algorithm: Fixed window — resets every 60 seconds.
/// Limit stored per API key ID in a ConcurrentDictionary of FixedWindowRateLimiters.
/// Limiters are reused across requests for the same key (not re-created per request).
///
/// Behaviour:
///   - Requests without an API key (UI routes) are NOT rate-limited.
///   - Requests exceeding the per-key limit return 429 Too Many Requests.
///   - Retry-After header is set to the seconds until the window resets.
/// </summary>
public class ApiKeyRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyRateLimitingMiddleware> _logger;

    // One limiter per API key ID.  ConcurrentDictionary ensures thread-safe access.
    // Limiters are never removed (API keys are never deleted, only revoked).
    // For a production deployment with thousands of keys, consider a bounded cache with eviction.
    private readonly ConcurrentDictionary<Guid, FixedWindowRateLimiter> _limiters = new();

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

        // Get or create a FixedWindowRateLimiter for this API key.
        var limiter = _limiters.GetOrAdd(apiKeyId, _ => CreateLimiter(rateLimitPerMinute));

        using var lease = await limiter.AcquireAsync(permitCount: 1, context.RequestAborted);

        if (lease.IsAcquired)
        {
            await _next(context);
            return;
        }

        // Rate limit exceeded — return 429.
        _logger.LogWarning(
            "Rate limit exceeded for API key {ApiKeyId}. Limit={LimitPerMinute}/min Path={Path}",
            apiKeyId, rateLimitPerMinute, context.Request.Path);

        // Calculate retry window seconds remaining (fixed window = 60 s)
        var retryAfterSeconds = 60;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            retryAfterSeconds = (int)retryAfter.TotalSeconds;

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

    private static FixedWindowRateLimiter CreateLimiter(int permitCountPerMinute) =>
        new(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitCountPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0   // No queue — reject immediately when limit exceeded
        });
}
