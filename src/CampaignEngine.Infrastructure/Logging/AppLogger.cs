using CampaignEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Logging;

/// <summary>
/// Infrastructure implementation of the application logging abstraction.
/// Wraps Microsoft.Extensions.Logging for structured logging support.
/// Downstream implementations (Serilog, NLog) plug in via ILogger registration.
/// </summary>
/// <typeparam name="T">The class or service context for log categorization.</typeparam>
public class AppLogger<T> : IAppLogger<T>
{
    private readonly ILogger<T> _logger;

    public AppLogger(ILogger<T> logger)
    {
        _logger = logger;
    }

    public void LogInformation(string message, params object[] args) =>
        _logger.LogInformation(message, args);

    public void LogWarning(string message, params object[] args) =>
        _logger.LogWarning(message, args);

    public void LogError(Exception exception, string message, params object[] args) =>
        _logger.LogError(exception, message, args);

    public void LogDebug(string message, params object[] args) =>
        _logger.LogDebug(message, args);

    public void LogCritical(Exception exception, string message, params object[] args) =>
        _logger.LogCritical(exception, message, args);
}
