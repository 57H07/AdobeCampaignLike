namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Validates attachment files against the type whitelist and size limits.
///
/// US-028 TASK-028-03: Attachment validation (type, size).
///
/// Business rules:
///   BR-1: Whitelist: PDF, DOCX, XLSX, PNG, JPG
///   BR-2: Max 10 MB per file
///   BR-3: Max 25 MB total across all attachments per send
/// </summary>
public interface IAttachmentValidationService
{
    /// <summary>
    /// Validates a single file against the extension whitelist and per-file size limit.
    /// </summary>
    /// <param name="fileName">File name (extension is used for whitelist check).</param>
    /// <param name="fileSizeBytes">File size in bytes.</param>
    /// <returns>
    /// A validation result with <see cref="AttachmentValidationResult.IsValid"/> set to false
    /// and <see cref="AttachmentValidationResult.ErrorMessage"/> populated on failure.
    /// </returns>
    AttachmentValidationResult ValidateFile(string fileName, long fileSizeBytes);

    /// <summary>
    /// Validates the combined total size of a set of attachments.
    /// </summary>
    /// <param name="totalSizeBytes">Sum of all attachment sizes in bytes.</param>
    /// <returns>
    /// A validation result indicating whether the total size is within the configured limit.
    /// </returns>
    AttachmentValidationResult ValidateTotalSize(long totalSizeBytes);
}

/// <summary>
/// Result of an attachment validation check.
/// </summary>
public sealed record AttachmentValidationResult(bool IsValid, string? ErrorMessage = null)
{
    /// <summary>A successful validation result.</summary>
    public static AttachmentValidationResult Ok() => new(true);

    /// <summary>A failed validation result with a descriptive message.</summary>
    public static AttachmentValidationResult Fail(string errorMessage) => new(false, errorMessage);
}
