namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the SQL Server data source connector.
/// Bound from appsettings.json section "SqlServerConnector".
///
/// Connection pooling is managed by ADO.NET automatically.
/// These options allow operators to tune pool behavior per environment.
/// </summary>
public sealed class SqlServerConnectorOptions
{
    public const string SectionName = "SqlServerConnector";

    /// <summary>
    /// Connection timeout in seconds for opening new connections.
    /// Default: 30 seconds (ADO.NET default).
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum number of connections to maintain in the pool.
    /// Default: 0 (ADO.NET default — no idle connections guaranteed).
    /// </summary>
    public int MinPoolSize { get; set; } = 0;

    /// <summary>
    /// Maximum number of connections in the pool.
    /// Default: 100 (ADO.NET default).
    /// Increase for high-throughput batch campaigns.
    /// </summary>
    public int MaxPoolSize { get; set; } = 100;
}
