using CampaignEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Logging;

/// <summary>
/// Infrastructure implementation of the application logging abstraction.
/// Wraps Microsoft.Extensions.Logging for structured logging support.
/// Serilog plugs in at the host level and receives these log entries
/// with full structured property support.
/// </summary>
/// <typeparam name="T">The class or service context for log categorization.</typeparam>
public class AppLogger<T> : IAppLogger<T>
{
    private readonly ILogger<T> _logger;

    public AppLogger(ILogger<T> logger)
    {
        _logger = logger;
    }

    // ----------------------------------------------------------------
    // Core log levels
    // ----------------------------------------------------------------

    public void LogDebug(string message, params object[] args) =>
        _logger.LogDebug(message, args);

    public void LogInformation(string message, params object[] args) =>
        _logger.LogInformation(message, args);

    public void LogWarning(string message, params object[] args) =>
        _logger.LogWarning(message, args);

    public void LogError(Exception exception, string message, params object[] args) =>
        _logger.LogError(exception, message, args);

    public void LogCritical(Exception exception, string message, params object[] args) =>
        _logger.LogCritical(exception, message, args);

    // ----------------------------------------------------------------
    // Performance logging
    // ----------------------------------------------------------------

    public void LogPerformance(
        string operationName,
        long elapsedMilliseconds,
        params (string Key, object Value)[] additionalProperties)
    {
        // Performance events use a fixed EventId (1000) to allow easy filtering
        using var scope = _logger.BeginScope(
            additionalProperties.ToDictionary(p => p.Key, p => p.Value));

        _logger.Log(
            elapsedMilliseconds > 1000 ? LogLevel.Warning : LogLevel.Information,
            new EventId(1000, "PerformanceMetric"),
            "Operation {OperationName} completed in {ElapsedMilliseconds}ms",
            operationName,
            elapsedMilliseconds);
    }

    // ----------------------------------------------------------------
    // Domain-specific helpers
    // ----------------------------------------------------------------

    public void LogDispatch(
        Guid campaignId,
        Guid templateId,
        string channel,
        string status,
        string? errorMessage = null)
    {
        if (errorMessage is null)
        {
            _logger.LogInformation(
                "Dispatch [{Status}] CampaignId={CampaignId} TemplateId={TemplateId} Channel={Channel}",
                status, campaignId, templateId, channel);
        }
        else
        {
            _logger.LogWarning(
                "Dispatch [{Status}] CampaignId={CampaignId} TemplateId={TemplateId} Channel={Channel} Error={ErrorMessage}",
                status, campaignId, templateId, channel, errorMessage);
        }
    }

    public void LogApiRequest(
        string method,
        string path,
        int statusCode,
        long elapsedMs,
        string? correlationId = null)
    {
        var level = statusCode >= 500 ? LogLevel.Error
            : statusCode >= 400 ? LogLevel.Warning
            : LogLevel.Information;

        _logger.Log(
            level,
            "API {Method} {Path} => {StatusCode} in {ElapsedMs}ms [CorrelationId={CorrelationId}]",
            method, path, statusCode, elapsedMs, correlationId ?? "-");
    }
}
