using System.Net;
using System.Text.Json;
using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Web.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Translates domain exceptions to appropriate HTTP responses.
/// Ensures no raw stack traces leak to clients in production.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail) = exception switch
        {
            NotFoundException notFound =>
                (HttpStatusCode.NotFound, "Resource Not Found", notFound.Message),
            ValidationException validation =>
                (HttpStatusCode.BadRequest, "Validation Error", validation.Message),
            DomainException domain =>
                (HttpStatusCode.UnprocessableEntity, "Business Rule Violation", domain.Message),
            _ =>
                (HttpStatusCode.InternalServerError, "Internal Server Error",
                    "An unexpected error occurred. Please try again later.")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for request {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception: {Title} for {Method} {Path}",
                title, context.Request.Method, context.Request.Path);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            status = (int)statusCode,
            title,
            detail,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
