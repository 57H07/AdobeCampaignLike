namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Result of a data source connectivity test.
/// </summary>
public class ConnectionTestResult
{
    /// <summary>True if the connection was established successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable message describing the outcome.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Duration of the test in milliseconds.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>UTC timestamp of the test.</summary>
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;

    public static ConnectionTestResult Ok(string message, long elapsedMs) =>
        new() { Success = true, Message = message, ElapsedMs = elapsedMs };

    public static ConnectionTestResult Fail(string message, long elapsedMs) =>
        new() { Success = false, Message = message, ElapsedMs = elapsedMs };
}
