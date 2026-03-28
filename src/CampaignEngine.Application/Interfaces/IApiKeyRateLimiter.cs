namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service that enforces per-API-key rate limiting and exposes
/// per-key usage statistics for monitoring and alerting.
///
/// Business rules (US-033):
///   BR-1: Default rate limit: 1000 requests/minute.
///   BR-2: Rate limit window: sliding 1-minute window.
///   BR-3: Per-key override stored on the ApiKey entity (RateLimitPerMinute).
///   BR-4: Exceeded requests are rejected; Retry-After indicates window reset time.
///   BR-5: X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset returned on every API response.
/// </summary>
public interface IApiKeyRateLimiter
{
    /// <summary>
    /// Attempts to acquire a rate-limit permit for the given API key.
    /// Returns a <see cref="RateLimitResult"/> describing whether the request
    /// is allowed and the current quota state.
    /// </summary>
    /// <param name="apiKeyId">The authenticated API key ID.</param>
    /// <param name="rateLimitPerMinute">The effective limit for this key (key override or system default).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RateLimitResult> TryAcquireAsync(
        Guid apiKeyId,
        int rateLimitPerMinute,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current rate-limit statistics for a specific API key.
    /// Returns null if the key has never made a request.
    /// </summary>
    ApiKeyRateLimitStats? GetStats(Guid apiKeyId);

    /// <summary>
    /// Returns rate-limit statistics for all tracked API keys.
    /// Used by the monitoring endpoint.
    /// </summary>
    IReadOnlyList<ApiKeyRateLimitStats> GetAllStats();

    /// <summary>
    /// Records a rejected request (rate limit exceeded) for monitoring counters.
    /// </summary>
    void RecordRejected(Guid apiKeyId);
}

/// <summary>
/// Result of a rate-limit permit acquisition attempt.
/// </summary>
public sealed record RateLimitResult
{
    /// <summary>True if the request is within quota and should proceed.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>The configured limit for this key (requests per minute).</summary>
    public required int Limit { get; init; }

    /// <summary>
    /// Remaining requests in the current window.
    /// 0 when the limit is exceeded.
    /// </summary>
    public required int Remaining { get; init; }

    /// <summary>
    /// UTC time when the current rate-limit window resets.
    /// Consumers can use this to schedule retries.
    /// </summary>
    public required DateTime ResetAt { get; init; }

    /// <summary>
    /// Seconds until the current window resets.
    /// Set only when IsAllowed = false (used for Retry-After header).
    /// </summary>
    public int RetryAfterSeconds => IsAllowed ? 0 : Math.Max(1, (int)(ResetAt - DateTime.UtcNow).TotalSeconds);
}

/// <summary>
/// Per-key rate-limit usage statistics snapshot for monitoring.
/// </summary>
public sealed record ApiKeyRateLimitStats
{
    /// <summary>The API key ID.</summary>
    public required Guid ApiKeyId { get; init; }

    /// <summary>The configured rate limit for this key (requests/minute).</summary>
    public required int LimitPerMinute { get; init; }

    /// <summary>Total requests processed since service start.</summary>
    public required long TotalRequests { get; init; }

    /// <summary>Total requests rejected (rate limit exceeded) since service start.</summary>
    public required long TotalRejected { get; init; }

    /// <summary>Requests in the current window (last 60 seconds).</summary>
    public required int RequestsInCurrentWindow { get; init; }

    /// <summary>Remaining quota in the current window.</summary>
    public int RemainingInCurrentWindow => Math.Max(0, LimitPerMinute - RequestsInCurrentWindow);

    /// <summary>UTC time when the current window resets.</summary>
    public required DateTime CurrentWindowResetAt { get; init; }
}
