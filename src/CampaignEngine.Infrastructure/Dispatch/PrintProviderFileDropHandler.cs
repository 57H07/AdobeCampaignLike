using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Writes consolidated PDF batch files and optional CSV manifests to a configured
/// output directory (UNC share or local path).
///
/// TASK-021-04: Print provider file drop handler.
///
/// Open Question Q5: Print provider format is unresolved.
/// This implementation uses a simple file drop (write to directory).
/// If the provider requires API upload instead, replace WriteAsync with an HTTP call.
///
/// File naming convention (BR-4): CAMPAIGN_{campaignId}_{timestamp}_{batchNumber}.pdf
/// Manifest naming:               CAMPAIGN_{campaignId}_{timestamp}_{batchNumber}_manifest.csv
/// </summary>
public sealed class PrintProviderFileDropHandler
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
    /// Writes a PDF batch and optional manifest to the output directory.
    /// Returns the full path of the written PDF file.
    ///
    /// Throws <see cref="LetterDispatchException"/> on I/O failure (always transient —
    /// a retry may succeed once the share is accessible again).
    /// </summary>
    /// <param name="pdfBytes">Consolidated PDF batch bytes.</param>
    /// <param name="manifestCsv">Optional CSV manifest content. Written when <see cref="LetterOptions.GenerateManifest"/> is true.</param>
    /// <param name="campaignId">Campaign identifier used in the file name.</param>
    /// <param name="batchNumber">Batch sequence number (1-based), appended to the file name.</param>
    /// <param name="timestamp">Timestamp used in the file name. Defaults to UtcNow when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full path of the written PDF file.</returns>
    public async Task<string> WriteAsync(
        byte[] pdfBytes,
        string? manifestCsv,
        Guid campaignId,
        int batchNumber,
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
        var prefix = _options.FileNamePrefix ?? "CAMPAIGN";
        var baseName = $"{prefix}_{campaignId:N}_{ts}_{batchNumber:D3}";

        try
        {
            // Ensure output directory exists (creates all intermediate directories).
            EnsureDirectoryExists(_options.OutputDirectory);

            var pdfPath = Path.Combine(_options.OutputDirectory, $"{baseName}.pdf");

            _logger.LogInformation(
                "Writing PDF batch file. Path={PdfPath} SizeBytes={Size} CampaignId={CampaignId} Batch={BatchNumber}",
                pdfPath,
                pdfBytes.Length,
                campaignId,
                batchNumber);

            await File.WriteAllBytesAsync(pdfPath, pdfBytes, cancellationToken);

            // Write manifest alongside the PDF when enabled and content is provided.
            if (_options.GenerateManifest && !string.IsNullOrEmpty(manifestCsv))
            {
                var manifestPath = Path.Combine(_options.OutputDirectory, $"{baseName}_manifest.csv");

                _logger.LogDebug(
                    "Writing CSV manifest. Path={ManifestPath} CampaignId={CampaignId} Batch={BatchNumber}",
                    manifestPath,
                    campaignId,
                    batchNumber);

                await File.WriteAllTextAsync(manifestPath, manifestCsv, cancellationToken);
            }

            _logger.LogInformation(
                "PDF batch file written successfully. Path={PdfPath} CampaignId={CampaignId}",
                pdfPath,
                campaignId);

            return pdfPath;
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
                "Failed to write PDF batch file to output directory '{Directory}'. CampaignId={CampaignId}",
                _options.OutputDirectory,
                campaignId);

            // I/O failures are treated as transient — the share may be temporarily unavailable.
            throw new LetterDispatchException(
                $"Failed to write PDF file to '{_options.OutputDirectory}': {ex.Message}",
                ex,
                isTransient: true);
        }
    }

    private static void EnsureDirectoryExists(string directory)
    {
        Directory.CreateDirectory(directory);
    }
}
