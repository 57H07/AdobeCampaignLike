namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration options for API key management and authentication.
/// Bound from appsettings.json section "ApiKey".
/// </summary>
public class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    /// <summary>
    /// Default key validity in days when ExpiresInDays is not specified.
    /// Default: 365 (1 year) per business rule.
    /// </summary>
    public int DefaultExpiresInDays { get; init; } = 365;

    /// <summary>
    /// Default rate limit per minute applied to new keys when not overridden.
    /// Default: 1000 requests/minute per business rule.
    /// </summary>
    public int DefaultRateLimitPerMinute { get; init; } = 1000;

    /// <summary>
    /// Whether to require HTTPS for all API key-authenticated requests.
    /// Default: true. Should only be disabled in development environments.
    /// </summary>
    public bool RequireHttps { get; init; } = true;
}
