using CampaignEngine.Application.DTOs.Campaigns;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Manages campaign attachment metadata and file storage.
///
/// US-028: Static and dynamic attachment management.
///
/// Coordinates validation, file upload to file share, and DB persistence of
/// <see cref="Domain.Entities.CampaignAttachment"/> records.
/// </summary>
public interface IAttachmentService
{
    /// <summary>
    /// Uploads a static attachment file, persists metadata, and returns the DTO.
    ///
    /// Business rules:
    ///   - Extension must be in the whitelist (PDF, DOCX, XLSX, PNG, JPG).
    ///   - File size must not exceed 10 MB.
    ///   - Campaign must exist.
    /// </summary>
    /// <param name="campaignId">Campaign to attach the file to.</param>
    /// <param name="fileName">Original file name from the upload.</param>
    /// <param name="content">File byte content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DTO of the created attachment record.</returns>
    Task<CampaignAttachmentDto> UploadStaticAsync(
        Guid campaignId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a dynamic attachment configuration for a campaign.
    ///
    /// Business rules:
    ///   - Campaign must exist.
    ///   - DynamicFieldName must be non-empty.
    ///   - IsDynamic = true, FilePath = empty (resolved at send time).
    /// </summary>
    /// <param name="campaignId">Campaign to configure.</param>
    /// <param name="dynamicFieldName">Data source field name holding per-recipient file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DTO of the created dynamic attachment record.</returns>
    Task<CampaignAttachmentDto> RegisterDynamicAsync(
        Guid campaignId,
        string dynamicFieldName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all attachments (static and dynamic) for a campaign.
    /// </summary>
    Task<IReadOnlyList<CampaignAttachmentDto>> GetByCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific attachment record and its associated file (if static).
    /// </summary>
    Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
