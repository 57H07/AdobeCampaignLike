using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Base class for channel-specific configuration options.
/// Each channel dispatcher should define a derived configuration class
/// and bind it from appsettings.json.
///
/// Example:
///   public class SmtpChannelConfiguration : ChannelConfigurationBase { ... }
///   services.Configure&lt;SmtpChannelConfiguration&gt;(config.GetSection("Channels:Email"));
/// </summary>
public abstract class ChannelConfigurationBase
{
    /// <summary>
    /// The channel type this configuration applies to.
    /// </summary>
    public abstract ChannelType Channel { get; }

    /// <summary>
    /// Whether this channel is enabled for dispatch.
    /// Disabled channels will not receive any messages.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of send attempts (including retries) before marking a send as permanently failed.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay in seconds between retry attempts (exponential backoff applied on top).
    /// </summary>
    public int[] RetryDelaysSeconds { get; set; } = [30, 120, 600];
}

/// <summary>
/// Configuration for the Email (SMTP) channel.
/// Bound from appsettings.json section "Channels:Email".
/// </summary>
public class EmailChannelConfiguration : ChannelConfigurationBase
{
    public const string SectionName = "Channels:Email";

    public override ChannelType Channel => ChannelType.Email;

    /// <summary>
    /// Maximum number of messages per second to avoid SMTP server throttling.
    /// </summary>
    public int ThrottlePerSecond { get; set; } = 100;
}

/// <summary>
/// Configuration for the SMS channel.
/// Bound from appsettings.json section "Channels:Sms".
/// </summary>
public class SmsChannelConfiguration : ChannelConfigurationBase
{
    public const string SectionName = "Channels:Sms";

    public override ChannelType Channel => ChannelType.Sms;

    /// <summary>
    /// Maximum number of messages per second per provider contract.
    /// </summary>
    public int ThrottlePerSecond { get; set; } = 10;

    /// <summary>
    /// Maximum character length for a single SMS message.
    /// </summary>
    public int MaxMessageLength { get; set; } = 160;
}

/// <summary>
/// Configuration for the Letter (PDF) channel.
/// Bound from appsettings.json section "Channels:Letter".
/// </summary>
public class LetterChannelConfiguration : ChannelConfigurationBase
{
    public const string SectionName = "Channels:Letter";

    public override ChannelType Channel => ChannelType.Letter;

    /// <summary>
    /// Maximum number of pages per consolidated PDF batch file.
    /// </summary>
    public int MaxPagesPerBatch { get; set; } = 500;

    /// <summary>
    /// Output directory (UNC path) where generated PDF files are written.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;
}
