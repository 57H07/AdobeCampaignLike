using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Writes DOCX files (one per recipient) to a configured output directory (UNC share or local path).
///
/// TASK-023-02: Print provider file drop handler for DOCX.
///
/// File naming convention (US-023 BR): {campaignId}_{recipientId}_{timestamp}.docx
/// </summary>
public class PrintProviderFileDropHandler
{
    private readonly LetterOptions _options;
    private readonly ILogger<PrintProviderFileDropHandler> _logger;

    public PrintProviderFileDropHandler(
        IOptions<LetterOptions> options,
        ILogger<PrintProviderFileDropHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Writes a single DOCX file for one recipient to the output directory.
    /// Returns the full path of the written file.
    ///
    /// TASK-023-02/03: One DOCX file per recipient, named {campaignId}_{recipientId}_{timestamp}.docx.
    ///
    /// Throws <see cref="LetterDispatchException"/> on I/O failure (always transient —
    /// a retry may succeed once the share is accessible again).
    /// </summary>
    /// <param name="docxBytes">DOCX file bytes.</param>
    /// <param name="campaignId">Campaign identifier used in the file name.</param>
    /// <param name="recipientId">Recipient identifier used in the file name.</param>
    /// <param name="timestamp">Timestamp used in the file name. Defaults to UtcNow when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full path of the written DOCX file.</returns>
    public virtual async Task<string> WriteFileAsync(
        byte[] docxBytes,
        Guid campaignId,
        string recipientId,
        DateTime? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OutputDirectory))
        {
            throw new LetterDispatchException(
                "Letter output directory is not configured. " +
                "Set 'Letter:OutputDirectory' in appsettings.json.",
                isTransient: false);
        }

        var ts = (timestamp ?? DateTime.UtcNow).ToString("yyyyMMddHHmmss");

        // Sanitize recipient ID to avoid path traversal or invalid file name characters.
        var safeRecipientId = SanitizeFileName(recipientId);
        var fileName = $"{campaignId:N}_{safeRecipientId}_{ts}.docx";

        try
        {
            // Ensure output directory exists (creates all intermediate directories).
            EnsureDirectoryExists(_options.OutputDirectory);

            var filePath = Path.Combine(_options.OutputDirectory, fileName);

            _logger.LogInformation(
                "Writing DOCX file. Path={FilePath} SizeBytes={Size} CampaignId={CampaignId} RecipientId={RecipientId}",
                filePath,
                docxBytes.Length,
                campaignId,
                recipientId);

            await File.WriteAllBytesAsync(filePath, docxBytes, cancellationToken);

            _logger.LogInformation(
                "DOCX file written successfully. Path={FilePath} CampaignId={CampaignId}",
                filePath,
                campaignId);

            return filePath;
        }
        catch (LetterDispatchException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write DOCX file to output directory '{Directory}'. CampaignId={CampaignId} RecipientId={RecipientId}",
                _options.OutputDirectory,
                campaignId,
                recipientId);

            // I/O failures are treated as transient — the share may be temporarily unavailable.
            throw new LetterDispatchException(
                $"Failed to write DOCX file to '{_options.OutputDirectory}': {ex.Message}",
                ex,
                isTransient: true);
        }
    }

    private static void EnsureDirectoryExists(string directory)
    {
        Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Replaces characters that are invalid in file names with underscores.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        return new string(chars);
    }
}
