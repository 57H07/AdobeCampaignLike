namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Root configuration for channel rate limiting (US-022).
/// Bound from appsettings.json section "CampaignEngine:RateLimit".
///
/// Example appsettings.json:
/// <code>
/// "CampaignEngine": {
///   "RateLimit": {
///     "Email": { "TokensPerSecond": 100, "MaxWaitTimeSeconds": 30 },
///     "Sms":   { "TokensPerSecond": 10,  "MaxWaitTimeSeconds": 10 },
///     "Letter":{ "TokensPerSecond": 0,   "MaxWaitTimeSeconds": 0  }
///   }
/// }
/// </code>
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "CampaignEngine:RateLimit";

    /// <summary>Rate limit settings for the Email (SMTP) channel.</summary>
    public ChannelRateLimitOptions Email { get; set; } = new()
    {
        TokensPerSecond = 100,
        MaxWaitTimeSeconds = 30
    };

    /// <summary>Rate limit settings for the SMS channel.</summary>
    public ChannelRateLimitOptions Sms { get; set; } = new()
    {
        TokensPerSecond = 10,
        MaxWaitTimeSeconds = 10
    };

    /// <summary>
    /// Rate limit settings for the Letter channel.
    /// Default is unlimited (TokensPerSecond = 0) per business rule BR-1.
    /// </summary>
    public ChannelRateLimitOptions Letter { get; set; } = new()
    {
        TokensPerSecond = 0,
        MaxWaitTimeSeconds = 0
    };
}

/// <summary>
/// Per-channel rate limit settings.
/// </summary>
public sealed class ChannelRateLimitOptions
{
    /// <summary>
    /// Maximum messages per second for this channel.
    /// Set to 0 (or omit) for unlimited throughput.
    /// </summary>
    public int TokensPerSecond { get; set; } = 0;

    /// <summary>
    /// Maximum time in seconds a caller may wait for a token before a
    /// <see cref="OperationCanceledException"/> is thrown (treated as transient failure).
    /// Set to 0 for unlimited wait (not recommended for production).
    /// Default: 30 seconds.
    /// </summary>
    public int MaxWaitTimeSeconds { get; set; } = 30;

    /// <summary>
    /// Burst capacity multiplier relative to TokensPerSecond.
    /// The bucket can hold up to <c>TokensPerSecond * BurstMultiplier</c> tokens.
    /// Default: 2 (allows short bursts of up to 2x the configured rate).
    /// </summary>
    public double BurstMultiplier { get; set; } = 2.0;
}
