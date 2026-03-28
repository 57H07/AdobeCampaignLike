namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Handles uploading attachment files to persistent storage (UNC file share).
///
/// US-028 TASK-028-02: File upload service.
///
/// Business rules:
///   - Files stored at a configurable UNC base path.
///   - Files organised in campaign-scoped sub-directories: {BasePath}/{campaignId}/{originalFileName}
///   - Upload returns the absolute stored path for DB persistence.
///   - Duplicate file names within the same campaign are disambiguated with a GUID prefix.
/// </summary>
public interface IFileUploadService
{
    /// <summary>
    /// Saves the uploaded file stream to the file share under a campaign-scoped directory.
    /// Returns the absolute stored path.
    /// </summary>
    /// <param name="campaignId">Campaign this attachment belongs to.</param>
    /// <param name="originalFileName">Original file name from the client upload.</param>
    /// <param name="content">File byte content to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Absolute path where the file was stored on the file share.</returns>
    Task<string> UploadAsync(
        Guid campaignId,
        string originalFileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a previously uploaded file from the file share.
    /// No-op if the file does not exist.
    /// </summary>
    /// <param name="filePath">Absolute path returned by a prior <see cref="UploadAsync"/> call.</param>
    Task DeleteAsync(string filePath);
}
