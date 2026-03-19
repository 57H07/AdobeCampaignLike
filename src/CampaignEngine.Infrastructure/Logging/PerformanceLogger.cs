using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Logging;

/// <summary>
/// Disposable helper that measures elapsed time and logs a performance metric
/// when disposed. Designed for use with C# <c>using</c> blocks.
///
/// Example:
/// <code>
///   using (new PerformanceLogger(_logger, "RenderTemplate", ("TemplateId", id)))
///   {
///       result = renderer.Render(template, data);
///   }
/// </code>
/// </summary>
public sealed class PerformanceLogger : IDisposable
{
    private static readonly EventId PerfEventId = new(1000, "PerformanceMetric");

    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly (string Key, object Value)[] _properties;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    /// <param name="logger">The logger instance to write to.</param>
    /// <param name="operationName">Human-readable name of the operation being timed.</param>
    /// <param name="properties">Optional additional structured key-value pairs added to the log entry.</param>
    public PerformanceLogger(
        ILogger logger,
        string operationName,
        params (string Key, object Value)[] properties)
    {
        _logger = logger;
        _operationName = operationName;
        _properties = properties;
        _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopwatch.Stop();
        var elapsed = _stopwatch.ElapsedMilliseconds;

        var level = elapsed > 1000 ? LogLevel.Warning : LogLevel.Information;

        using var scope = _logger.BeginScope(
            _properties.ToDictionary(p => p.Key, p => p.Value));

        _logger.Log(
            level,
            PerfEventId,
            "Operation {OperationName} completed in {ElapsedMilliseconds}ms",
            _operationName,
            elapsed);
    }
}
