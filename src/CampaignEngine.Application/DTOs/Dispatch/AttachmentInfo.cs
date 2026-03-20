namespace CampaignEngine.Application.DTOs.Dispatch;

/// <summary>
/// Attachment data to be included with a dispatched message.
/// Extended in US-019 (TASK-019-03) to support loading from file paths.
///
/// Business rules:
///   - Whitelist: PDF, DOCX, XLSX, PNG, JPG
///   - Max 10 MB per file
///   - Max 25 MB total across all attachments
/// </summary>
public class AttachmentInfo
{
    /// <summary>Display file name (used in the email attachment header).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME content type (e.g. "application/pdf", "image/png").</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Raw binary content of the attachment.
    /// Populated either directly or via <see cref="FilePath"/> during dispatch.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// Optional absolute file path on disk.
    /// When set, the EmailDispatcher loads the file content at send time.
    /// TASK-019-03: attachment handling from file paths.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Creates an AttachmentInfo from a file path on disk.
    /// The FileName and MimeType are derived from the path.
    /// </summary>
    public static AttachmentInfo FromFilePath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var mimeType = GetMimeType(Path.GetExtension(filePath));
        return new AttachmentInfo
        {
            FileName = fileName,
            MimeType = mimeType,
            FilePath = filePath,
            Data = []
        };
    }

    /// <summary>
    /// Creates an AttachmentInfo from raw byte content.
    /// </summary>
    public static AttachmentInfo FromBytes(string fileName, byte[] data, string mimeType)
    {
        return new AttachmentInfo
        {
            FileName = fileName,
            MimeType = mimeType,
            Data = data
        };
    }

    /// <summary>Returns the MIME type for a given file extension.</summary>
    public static string GetMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf"  => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".png"  => "image/png",
        ".jpg"  => "image/jpeg",
        ".jpeg" => "image/jpeg",
        _       => "application/octet-stream"
    };
}
