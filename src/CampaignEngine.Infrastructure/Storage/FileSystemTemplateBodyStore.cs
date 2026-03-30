using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Exceptions;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Storage;

/// <summary>
/// File-system implementation of <see cref="ITemplateBodyStore"/>.
///
/// US-002 TASK-002-01/02/03/04: FileSystemTemplateBodyStore implementation.
///
/// Stores template body files (HTML for Email/SMS, DOCX for Letter) under a
/// configurable root directory. The <c>path</c> parameter for each operation is
/// treated as a path relative to <see cref="TemplateBodyStoreOptions.RootPath"/>.
///
/// Atomic write: content is written to a temporary <c>.tmp</c> file in the same
/// directory, then renamed atomically with <see cref="File.Move"/> (overwrite=true).
/// This prevents readers from observing a partially-written file.
///
/// Exception mapping:
///   - Null/empty path          → <see cref="TemplateBodyNotFoundException"/>
///   - File not found           → <see cref="TemplateBodyNotFoundException"/>
///   - Any other I/O exception  → <see cref="TemplateBodyCorruptedException"/>
/// </summary>
public sealed class FileSystemTemplateBodyStore : ITemplateBodyStore
{
    private readonly string _rootPath;
    private readonly IAppLogger<FileSystemTemplateBodyStore> _logger;

    public FileSystemTemplateBodyStore(
        IOptions<TemplateBodyStoreOptions> options,
        IAppLogger<FileSystemTemplateBodyStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _rootPath = options.Value.RootPath;
        _logger = logger;
    }

    // ----------------------------------------------------------------
    // WriteAsync — TASK-002-02
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<string> WriteAsync(
        string path,
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = ResolveFull(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var tmpPath = fullPath + ".tmp";

        try
        {
            Directory.CreateDirectory(directory);

            // Write to temp file first (atomic write pattern)
            await using (var fs = new FileStream(
                tmpPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous))
            {
                await content.CopyToAsync(fs, ct);
                await fs.FlushAsync(ct);
            }

            // Atomically replace destination (overwrite=true)
            File.Move(tmpPath, fullPath, overwrite: true);

            _logger.LogInformation(
                "Template body written: Path={Path}, Size={SizeBytes} bytes",
                fullPath, new FileInfo(fullPath).Length);

            return path;
        }
        catch (OperationCanceledException)
        {
            // Clean up partial temp file on cancellation
            TryDeleteSilently(tmpPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteSilently(tmpPath);

            _logger.LogError(
                ex,
                "Failed to write template body to path: {Path}",
                fullPath);

            throw new TemplateBodyCorruptedException(path, ex);
        }
    }

    // ----------------------------------------------------------------
    // ReadAsync — TASK-002-03
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public Task<Stream> ReadAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromException<Stream>(new TemplateBodyNotFoundException(path));

        var fullPath = ResolveFull(path);

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning(
                "Template body file not found: {Path}", fullPath);

            throw new TemplateBodyNotFoundException(path);
        }

        try
        {
            // Return a FileStream; the caller is responsible for disposing it.
            Stream stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult(stream);
        }
        catch (FileNotFoundException ex)
        {
            // Race condition: file disappeared between Exists check and Open
            _logger.LogWarning(
                "Template body file vanished between existence check and open: {Path}",
                fullPath);

            throw new TemplateBodyNotFoundException(path, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to open template body at path: {Path}",
                fullPath);

            throw new TemplateBodyCorruptedException(path, ex);
        }
    }

    // ----------------------------------------------------------------
    // DeleteAsync — TASK-002-04
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.CompletedTask;

        var fullPath = ResolveFull(path);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);

                _logger.LogInformation(
                    "Template body deleted: {Path}", fullPath);
            }
            else
            {
                _logger.LogDebug(
                    "DeleteAsync: file not found (no-op): {Path}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete template body at path: {Path}",
                fullPath);

            // Surface I/O failures so callers can decide how to handle them
            throw new TemplateBodyCorruptedException(path, ex);
        }

        return Task.CompletedTask;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private string ResolveFull(string relativePath)
        => Path.GetFullPath(Path.Combine(_rootPath, relativePath));

    private static void TryDeleteSilently(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Best-effort cleanup — do not propagate
        }
    }
}
