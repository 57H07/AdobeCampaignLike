using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Attachments;

/// <summary>
/// Writes attachment files to a UNC file share (or local directory).
///
/// US-028 TASK-028-02: File upload service.
///
/// Storage layout:  {BasePath}/{campaignId}/{guid}_{originalFileName}
/// The GUID prefix prevents collisions when the same file name is uploaded twice
/// for the same campaign.
///
/// Thread-safety: Directory.CreateDirectory is idempotent and thread-safe on Windows.
/// File.WriteAllBytesAsync uses a new FileStream per call — no shared state.
/// </summary>
public sealed class FileUploadService : IFileUploadService
{
    private readonly AttachmentStorageOptions _options;
    private readonly IAppLogger<FileUploadService> _logger;

    public FileUploadService(
        IOptions<AttachmentStorageOptions> options,
        IAppLogger<FileUploadService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        Guid campaignId,
        string originalFileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentNullException.ThrowIfNull(content);

        // Campaign-scoped sub-directory
        var campaignDir = Path.Combine(_options.BasePath, campaignId.ToString());
        Directory.CreateDirectory(campaignDir);

        // Disambiguate with GUID prefix to prevent collisions
        var safeFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(originalFileName)}";
        var destination = Path.Combine(campaignDir, safeFileName);

        await File.WriteAllBytesAsync(destination, content, cancellationToken);

        _logger.LogInformation(
            "Attachment uploaded: Campaign={CampaignId}, File={FileName}, Size={SizeBytes} bytes, Path={Path}",
            campaignId, originalFileName, content.Length, destination);

        return destination;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.CompletedTask;

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Attachment deleted: {Path}", filePath);
        }
        else
        {
            _logger.LogWarning("Delete requested for non-existent attachment file: {Path}", filePath);
        }

        return Task.CompletedTask;
    }
}
