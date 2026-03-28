namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the REST API data source connector.
/// Bound from appsettings.json section "RestApiConnector".
///
/// These options govern global HTTP client behavior (timeouts, retries, size limits).
/// Per-datasource authentication and endpoint settings are carried in the connection string.
/// </summary>
public sealed class RestApiConnectorOptions
{
    public const string SectionName = "RestApiConnector";

    /// <summary>
    /// HTTP request timeout in seconds.
    /// Business rule: 60 seconds (US-017).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of retry attempts on transient HTTP failures.
    /// Business rule: 3 attempts with exponential backoff (US-017).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in seconds for exponential backoff between retries.
    /// Retry n uses: BaseRetryDelaySeconds * 2^(n-1).
    /// E.g., 1s, 2s, 4s for the default value of 1.
    /// </summary>
    public int BaseRetryDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Maximum response body size in bytes.
    /// Business rule: 50 MB (US-017).
    /// </summary>
    public long MaxResponseSizeBytes { get; set; } = 50 * 1024 * 1024; // 50 MB

    /// <summary>
    /// Maximum number of pages to fetch when paginating.
    /// Guards against infinite loops on misconfigured APIs.
    /// Default: 1000 pages.
    /// </summary>
    public int MaxPages { get; set; } = 1000;
}
