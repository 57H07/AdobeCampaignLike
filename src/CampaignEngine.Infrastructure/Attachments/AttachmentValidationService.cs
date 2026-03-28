using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Attachments;

/// <summary>
/// Validates attachment files against the extension whitelist and size limits.
///
/// US-028 TASK-028-03: Attachment validation (type, size).
///
/// Business rules enforced:
///   BR-1: Whitelist — PDF, DOCX, XLSX, PNG, JPG (configurable via AttachmentStorageOptions).
///   BR-2: Max 10 MB per file (configurable via AttachmentStorageOptions.MaxFileSizeBytes).
///   BR-3: Max 25 MB total across all attachments per send (MaxTotalSizeBytes).
/// </summary>
public sealed class AttachmentValidationService : IAttachmentValidationService
{
    private readonly AttachmentStorageOptions _options;

    public AttachmentValidationService(IOptions<AttachmentStorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public AttachmentValidationResult ValidateFile(string fileName, long fileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return AttachmentValidationResult.Fail("File name must not be empty.");

        // Extension whitelist (BR-1)
        var ext = Path.GetExtension(fileName);
        var allowedSet = new HashSet<string>(_options.AllowedExtensions, StringComparer.OrdinalIgnoreCase);

        if (!allowedSet.Contains(ext))
        {
            return AttachmentValidationResult.Fail(
                $"File extension '{ext}' is not allowed. " +
                $"Allowed extensions: {string.Join(", ", _options.AllowedExtensions)}.");
        }

        // Per-file size limit (BR-2)
        if (fileSizeBytes <= 0)
            return AttachmentValidationResult.Fail("File must not be empty (0 bytes).");

        if (fileSizeBytes > _options.MaxFileSizeBytes)
        {
            var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
            var actualMb = fileSizeBytes / (1024.0 * 1024.0);
            return AttachmentValidationResult.Fail(
                $"File '{fileName}' is too large ({actualMb:F1} MB). " +
                $"Maximum allowed size per file is {maxMb:F0} MB.");
        }

        return AttachmentValidationResult.Ok();
    }

    /// <inheritdoc />
    public AttachmentValidationResult ValidateTotalSize(long totalSizeBytes)
    {
        if (totalSizeBytes > _options.MaxTotalSizeBytes)
        {
            var maxMb = _options.MaxTotalSizeBytes / (1024.0 * 1024.0);
            var actualMb = totalSizeBytes / (1024.0 * 1024.0);
            return AttachmentValidationResult.Fail(
                $"Total attachment size ({actualMb:F1} MB) exceeds the maximum of {maxMb:F0} MB per send.");
        }

        return AttachmentValidationResult.Ok();
    }
}
