using Serilog.Context;

namespace CampaignEngine.Web.Middleware;

/// <summary>
/// Middleware that extracts or generates a correlation ID for each HTTP request.
/// The correlation ID is:
///   1. Read from the incoming "X-Correlation-Id" header if present (caller-supplied).
///   2. Generated as a new GUID if not present.
///   3. Written back to the "X-Correlation-Id" response header.
///   4. Pushed into the Serilog LogContext so all log entries in this request
///      carry the {CorrelationId} property automatically.
/// </summary>
public class CorrelationIdMiddleware
{
    /// <summary>Name of the HTTP header used to propagate correlation IDs.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        // Expose the correlation ID on the response header immediately
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        // Store in HttpContext.Items so other middleware / controllers can read it
        context.Items[CorrelationIdHeader] = correlationId;

        // Push into Serilog's LogContext so every log entry in this request
        // automatically has the CorrelationId property attached
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            _logger.LogDebug(
                "Request {Method} {Path} received with CorrelationId {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                correlationId);

            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var existingId)
            && !string.IsNullOrWhiteSpace(existingId))
        {
            return existingId.ToString();
        }

        return Guid.NewGuid().ToString("D");
    }
}
