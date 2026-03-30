using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Startup;

/// <summary>
/// Validates the template storage root directory at application startup.
///
/// US-004 TASK-004-03/04/05: IHostedService that halts startup if RootPath is
/// null/empty, does not exist, or is not writable.
///
/// Validation runs inside <see cref="StartAsync"/> which is called by the generic
/// host before the HTTP server begins accepting requests, satisfying BR-1:
/// "Validation runs before any web requests are accepted."
///
/// On failure, a <see cref="InvalidOperationException"/> is thrown, which causes
/// the host to terminate with a non-zero exit code (BR-2: "Misconfiguration must
/// halt startup, not just log a warning").
/// </summary>
public sealed class TemplateStorageStartupValidator : IHostedService
{
    private const string TempCheckFileName = ".startup_check";

    private readonly TemplateStorageOptions _options;
    private readonly IAppLogger<TemplateStorageStartupValidator> _logger;

    public TemplateStorageStartupValidator(
        IOptions<TemplateStorageOptions> options,
        IAppLogger<TemplateStorageStartupValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs validation checks on the configured storage root.
    /// Throws <see cref="InvalidOperationException"/> if any check fails.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateRootPath(_options.RootPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ----------------------------------------------------------------
    // Internal validation — factored out for unit-test accessibility
    // ----------------------------------------------------------------

    /// <summary>
    /// Validates that <paramref name="rootPath"/> is non-empty, exists, and is writable.
    /// Throws <see cref="InvalidOperationException"/> on the first failing check.
    /// Exposed as public for unit-test accessibility without requiring InternalsVisibleTo.
    /// </summary>
    public void ValidateRootPath(string rootPath)
    {
        // TASK-004-04: Check path is not null or empty
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            var message = "TemplateStorage:RootPath is not configured. " +
                          "Set a valid directory path in appsettings.json under TemplateStorage:RootPath.";
            var ex = new InvalidOperationException(message);
            _logger.LogError(ex, message);
            throw ex;
        }

        // TASK-004-04: Check directory exists
        if (!Directory.Exists(rootPath))
        {
            var message = $"TemplateStorage:RootPath '{rootPath}' does not exist. " +
                          "Create the directory or update the path in appsettings.json.";
            var ex = new InvalidOperationException(message);
            _logger.LogError(ex, message);
            throw ex;
        }

        // TASK-004-05: Check directory is writable by creating and deleting a temp file
        var tempFile = Path.Combine(rootPath, TempCheckFileName);
        try
        {
            File.WriteAllText(tempFile, "startup_check");
            File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            var message = $"TemplateStorage:RootPath '{rootPath}' is not writable. " +
                          $"Check directory permissions. Inner error: {ex.Message}";
            var ioEx = new InvalidOperationException(message, ex);
            _logger.LogError(ioEx, message);
            throw ioEx;
        }

        _logger.LogInformation(
            "TemplateStorage startup validation passed. RootPath='{RootPath}'",
            rootPath);
    }
}
