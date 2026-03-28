namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration for attachment file storage (UNC file share).
///
/// US-028 TASK-028-02: File upload service configuration.
/// Q7: File share infrastructure — configured via "CampaignEngine:Attachments:Storage" section.
///
/// Supports UNC paths (\\server\share\attachments), local paths, and NAS mounts.
/// The base path must be writable by the application service account.
/// </summary>
public class AttachmentStorageOptions
{
    public const string SectionName = "CampaignEngine:Attachments:Storage";

    /// <summary>
    /// Base directory where attachment files are stored.
    /// Supports UNC paths (\\server\share\path) and local absolute paths.
    /// Defaults to a local "attachments" subfolder relative to the app root.
    /// </summary>
    public string BasePath { get; set; } = "attachments";

    /// <summary>
    /// Maximum allowed file size in bytes for a single attachment.
    /// Default: 10 MB.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed total size in bytes for all attachments per send.
    /// Default: 25 MB.
    /// </summary>
    public long MaxTotalSizeBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Allowed file extensions (whitelist).
    /// Business rule 1: PDF, DOCX, XLSX, PNG, JPG.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
        [".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg"];
}
