namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Hangfire configuration options bound from appsettings.json "Hangfire" section.
/// </summary>
public class HangfireOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>URL path for the Hangfire dashboard. Default: /hangfire</summary>
    public string DashboardPath { get; set; } = "/hangfire";

    /// <summary>Number of parallel worker threads. Default: 8 (4-8 per spec).</summary>
    public int WorkerCount { get; set; } = 8;
}
