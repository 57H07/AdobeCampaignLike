namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Root strongly-typed configuration options for the CampaignEngine application.
/// Bound from appsettings.json section "CampaignEngine".
/// </summary>
public class CampaignEngineOptions
{
    public const string SectionName = "CampaignEngine";

    public BatchProcessingOptions BatchProcessing { get; set; } = new();
    public DispatchOptions Dispatch { get; set; } = new();
    public AttachmentOptions Attachments { get; set; } = new();
    public RenderingOptions Rendering { get; set; } = new();
    public SendLogOptions SendLog { get; set; } = new();
}

public class BatchProcessingOptions
{
    public int ChunkSize { get; set; } = 500;
    public int WorkerCount { get; set; } = 8;
    public int MaxRetryAttempts { get; set; } = 3;
    public int[] RetryDelaysSeconds { get; set; } = [30, 120, 600];
}

public class DispatchOptions
{
    public ChannelThrottleOptions Email { get; set; } = new() { ThrottlePerSecond = 100 };
    public ChannelThrottleOptions Sms { get; set; } = new() { ThrottlePerSecond = 10 };
}

public class ChannelThrottleOptions
{
    public int ThrottlePerSecond { get; set; }
}

public class AttachmentOptions
{
    public string[] AllowedExtensions { get; set; } = [".pdf", ".docx", ".xlsx", ".png", ".jpg"];
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    public long MaxTotalSizeBytes { get; set; } = 25 * 1024 * 1024; // 25 MB
}

public class RenderingOptions
{
    public int TimeoutSeconds { get; set; } = 10;
}

public class SendLogOptions
{
    public int RetentionDays { get; set; } = 90;
}
