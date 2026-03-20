using CampaignEngine.Application.Interfaces;
using System.Security.Claims;

namespace CampaignEngine.Web.Middleware;

/// <summary>
/// Middleware that authenticates API requests using an API key passed in the
/// X-Api-Key request header.
///
/// If a valid key is found, a ClaimsPrincipal is synthesised and set on
/// HttpContext.User so that downstream [Authorize] attributes are satisfied.
///
/// If no X-Api-Key header is present, the request falls through to the next
/// middleware unchanged — standard cookie/Identity authentication handles UI routes.
///
/// If an X-Api-Key header IS present but the key is invalid (not found, revoked,
/// or expired), a 401 Unauthorized is returned immediately.
///
/// Business rules:
///   - API key passed via header: X-Api-Key: &lt;key&gt;
///   - Invalid or expired keys → 401 Unauthorized
///   - Valid keys create an "ApiKeyUser" identity with the ApiConsumer role
///     so API endpoints can use [Authorize(Roles = "ApiConsumer")] or
///     simply [Authorize] (authenticated user check).
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only attempt API key authentication if the header is present.
        // UI routes (cookie-authenticated) don't include this header.
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var rawKeyValues))
        {
            await _next(context);
            return;
        }

        var plaintextKey = rawKeyValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                status = 401,
                title = "Unauthorized",
                detail = "The X-Api-Key header is empty."
            });
            return;
        }

        // Resolve IApiKeyService from the per-request scope (scoped service)
        var apiKeyService = context.RequestServices.GetRequiredService<IApiKeyService>();

        var apiKey = await apiKeyService.ValidateAsync(plaintextKey, context.RequestAborted);

        if (apiKey is null)
        {
            _logger.LogWarning(
                "API key authentication failed — invalid, expired, or revoked key. " +
                "Path={Path} RemoteIp={RemoteIp}",
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                status = 401,
                title = "Unauthorized",
                detail = "The provided API key is invalid, expired, or has been revoked."
            });
            return;
        }

        // Build a synthetic ClaimsPrincipal for the authenticated API consumer.
        // The identity's name is the API key's human-readable label.
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, apiKey.Name),
            new Claim(ClaimTypes.NameIdentifier, apiKey.Id.ToString()),
            new Claim(ClaimTypes.Role, "ApiConsumer"),
            // Expose the effective rate limit for downstream rate-limiter middleware
            new Claim("RateLimitPerMinute",
                (apiKey.RateLimitPerMinute ?? ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute).ToString())
        };

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.AuthenticationScheme);
        context.User = new ClaimsPrincipal(identity);

        // Store the API key ID on the context items for rate-limiting middleware
        context.Items["ApiKeyId"] = apiKey.Id;
        context.Items["ApiKeyRateLimitPerMinute"] =
            apiKey.RateLimitPerMinute ?? ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute;

        _logger.LogDebug(
            "API key authenticated — KeyId={KeyId} KeyName={KeyName} Path={Path}",
            apiKey.Id, apiKey.Name, context.Request.Path);

        await _next(context);
    }
}

/// <summary>
/// Constants for API key authentication.
/// </summary>
public static class ApiKeyAuthenticationDefaults
{
    /// <summary>Authentication scheme name used in ClaimsIdentity.</summary>
    public const string AuthenticationScheme = "ApiKey";

    /// <summary>Default rate limit per minute when the key has no override.</summary>
    public const int DefaultRateLimitPerMinute = 1000;

    /// <summary>HTTP header name carrying the API key.</summary>
    public const string HeaderName = "X-Api-Key";
}
