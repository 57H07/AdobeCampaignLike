namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Application-level logging abstraction.
/// Provides structured logging with cross-cutting concern awareness.
/// Implemented in Infrastructure layer with Serilog.
///
/// Usage convention:
///   - Inject IAppLogger{T} where T is the declaring class.
///   - Use structured message templates (not string interpolation):
///     LogInformation("Processing campaign {CampaignId} with {RecipientCount} recipients", id, count)
///   - PII fields must NOT appear in log messages — mask or omit them.
/// </summary>
/// <typeparam name="T">The class or service context for log categorization.</typeparam>
public interface IAppLogger<T>
{
    // ----------------------------------------------------------------
    // Core log levels
    // ----------------------------------------------------------------

    void LogDebug(string message, params object[] args);

    void LogInformation(string message, params object[] args);

    void LogWarning(string message, params object[] args);

    void LogError(Exception exception, string message, params object[] args);

    void LogCritical(Exception exception, string message, params object[] args);

    // ----------------------------------------------------------------
    // Performance logging
    // ----------------------------------------------------------------

    /// <summary>
    /// Logs the completion of a timed operation at Information level.
    /// Caller is responsible for measuring the elapsed time.
    /// </summary>
    /// <param name="operationName">Descriptive name of the timed operation.</param>
    /// <param name="elapsedMilliseconds">Measured duration in milliseconds.</param>
    /// <param name="additionalProperties">Optional additional structured properties.</param>
    void LogPerformance(string operationName, long elapsedMilliseconds,
        params (string Key, object Value)[] additionalProperties);

    // ----------------------------------------------------------------
    // Domain-specific helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Logs a campaign dispatch event correlated to a campaign and template.
    /// </summary>
    void LogDispatch(Guid campaignId, Guid templateId, string channel,
        string status, string? errorMessage = null);

    /// <summary>
    /// Logs an API request boundary event (called by middleware or action filters).
    /// </summary>
    void LogApiRequest(string method, string path, int statusCode, long elapsedMs,
        string? correlationId = null);
}
