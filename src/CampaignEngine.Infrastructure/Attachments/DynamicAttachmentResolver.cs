using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;

namespace CampaignEngine.Infrastructure.Attachments;

/// <summary>
/// Resolves dynamic attachment file paths from recipient data at send time.
///
/// US-028 TASK-028-04: Dynamic attachment resolver (from data field).
///
/// Business rules enforced:
///   BR-4: Dynamic attachments resolved at send time from recipient data field.
///   BR-5: Missing dynamic attachments: log warning, send without attachment.
///
/// The resolver combines static attachments (pre-loaded from DB) with dynamic attachments
/// (file path read from a recipient data field). If the dynamic field is missing, null,
/// or the file does not exist on disk, a warning is logged and the attachment is skipped.
/// This ensures a missing document never blocks the overall send.
/// </summary>
public sealed class DynamicAttachmentResolver : IDynamicAttachmentResolver
{
    private readonly IAppLogger<DynamicAttachmentResolver> _logger;

    public DynamicAttachmentResolver(IAppLogger<DynamicAttachmentResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<AttachmentInfo> Resolve(
        IReadOnlyList<AttachmentInfo> staticAttachments,
        string? dynamicFieldName,
        IDictionary<string, object?> recipientData)
    {
        ArgumentNullException.ThrowIfNull(recipientData);
        ArgumentNullException.ThrowIfNull(staticAttachments);

        var result = new List<AttachmentInfo>(staticAttachments);

        // No dynamic attachment configured — return static list only
        if (string.IsNullOrWhiteSpace(dynamicFieldName))
            return result;

        // Attempt to read the field value from recipient data (BR-4)
        if (!recipientData.TryGetValue(dynamicFieldName, out var fieldValue) || fieldValue is null)
        {
            // BR-5: missing field — log warning, do not fail
            _logger.LogWarning(
                "Dynamic attachment field '{FieldName}' not found in recipient data. " +
                "Sending without dynamic attachment.",
                dynamicFieldName);
            return result;
        }

        var filePath = fieldValue.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            // BR-5: empty field value — log warning, do not fail
            _logger.LogWarning(
                "Dynamic attachment field '{FieldName}' is empty for this recipient. " +
                "Sending without dynamic attachment.",
                dynamicFieldName);
            return result;
        }

        // Verify the file exists at the specified path (BR-5: missing file is non-fatal)
        if (!File.Exists(filePath))
        {
            _logger.LogWarning(
                "Dynamic attachment file not found at path '{FilePath}' " +
                "(field '{FieldName}'). Sending without dynamic attachment.",
                filePath, dynamicFieldName);
            return result;
        }

        // Build AttachmentInfo from the resolved file path
        var dynamicAttachment = AttachmentInfo.FromFilePath(filePath);
        result.Add(dynamicAttachment);

        _logger.LogInformation(
            "Dynamic attachment resolved: Field='{FieldName}', Path='{FilePath}'",
            dynamicFieldName, filePath);

        return result;
    }
}
