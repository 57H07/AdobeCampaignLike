namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Application-level logging abstraction.
/// Provides structured logging with cross-cutting concern awareness.
/// Implemented in Infrastructure layer with Serilog/NLog.
/// </summary>
/// <typeparam name="T">The class or service context for logging.</typeparam>
public interface IAppLogger<T>
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(Exception exception, string message, params object[] args);
    void LogDebug(string message, params object[] args);
    void LogCritical(Exception exception, string message, params object[] args);
}
