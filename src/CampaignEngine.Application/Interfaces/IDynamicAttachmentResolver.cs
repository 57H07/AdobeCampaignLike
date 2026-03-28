using CampaignEngine.Application.DTOs.Dispatch;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Resolves dynamic attachment file paths for a single recipient send.
///
/// US-028 TASK-028-04: Dynamic attachment resolver (from data field).
///
/// Business rules:
///   BR-4: Dynamic attachments resolved at send time from recipient data field.
///   BR-5: Missing dynamic attachments: log warning, send without attachment.
/// </summary>
public interface IDynamicAttachmentResolver
{
    /// <summary>
    /// Resolves all attachments (static + dynamic) for a single recipient send.
    ///
    /// Static attachments are returned directly from <paramref name="staticAttachments"/>.
    /// Dynamic attachments are resolved by looking up the configured field name in
    /// <paramref name="recipientData"/>. Missing or empty field values are logged as
    /// warnings but do not prevent the send.
    /// </summary>
    /// <param name="staticAttachments">
    /// Pre-loaded static attachment infos (from DB record file paths).
    /// </param>
    /// <param name="dynamicFieldName">
    /// Data source field name containing the per-recipient file path.
    /// Null means no dynamic attachment configured.
    /// </param>
    /// <param name="recipientData">
    /// Recipient data row from the data source.
    /// </param>
    /// <returns>
    /// Combined list of resolved <see cref="AttachmentInfo"/> objects ready for dispatch.
    /// </returns>
    IReadOnlyList<AttachmentInfo> Resolve(
        IReadOnlyList<AttachmentInfo> staticAttachments,
        string? dynamicFieldName,
        IDictionary<string, object?> recipientData);
}
