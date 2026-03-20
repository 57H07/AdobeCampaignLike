namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// SMTP server configuration options.
/// Bound from appsettings.json section "Smtp".
///
/// TASK-019-02: SMTP configuration model.
/// Supports: server, port, credentials, TLS, From address, optional Reply-To.
/// Business rules:
///   - From address configurable per environment
///   - Reply-To address optional per campaign (can be overridden at send time)
///   - Attachment whitelist enforced: PDF, DOCX, XLSX, PNG, JPG
///   - Attachment size limits: 10 MB per file, 25 MB total
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>SMTP server hostname or IP address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP server port (587 = STARTTLS, 465 = SMTPS, 25 = plain).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to use SSL/TLS for the connection.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>SMTP authentication username.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>SMTP authentication password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Default From address (configurable per environment). Business rule 1.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Display name shown alongside the From address.</summary>
    public string FromName { get; set; } = "CampaignEngine";

    /// <summary>
    /// Optional default Reply-To address. Can be overridden per campaign.
    /// Business rule 2: Reply-To optional per campaign.
    /// </summary>
    public string? ReplyToAddress { get; set; }

    /// <summary>Timeout in seconds for SMTP connect and send operations.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of connection attempts before failing with a transient error.</summary>
    public int MaxConnectRetries { get; set; } = 3;

    /// <summary>
    /// Allowed attachment MIME types (whitelist).
    /// Business rule 3: PDF, DOCX, XLSX, PNG, JPG.
    /// </summary>
    public string[] AllowedAttachmentExtensions { get; set; } =
        [".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg"];

    /// <summary>Maximum size in bytes for a single attachment (default 10 MB). Business rule 4.</summary>
    public long MaxAttachmentFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum total size in bytes for all attachments per email (default 25 MB). Business rule 4.</summary>
    public long MaxAttachmentTotalSizeBytes { get; set; } = 25 * 1024 * 1024;
}
