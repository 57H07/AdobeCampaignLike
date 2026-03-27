using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using System.Net.Sockets;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Classifies exceptions as transient (retriable) or permanent (non-retriable)
/// for SMTP and SMS dispatch operations.
///
/// TASK-035-02: Transient failure detection (SMTP, SMS errors).
///
/// Transient (retriable):
///   - SMTP: connection timeout, server temporarily unavailable (4xx), protocol errors, socket errors
///   - SMS: provider rate limit (HTTP 429), network timeout, server error (HTTP 5xx)
///   - General: OperationCanceledException (except deliberate cancellations), SocketException
///
/// Permanent (not retriable):
///   - SMTP: invalid recipient (5xx reject), authentication failure, attachment validation
///   - SMS: invalid phone number (HTTP 400), account disabled (HTTP 403)
///   - Template: rendering errors
///   - Missing dispatcher registration
/// </summary>
public class TransientFailureDetector : ITransientFailureDetector
{
    // SMS HTTP status codes that indicate transient failures
    private static readonly HashSet<int> TransientHttpStatusCodes = [408, 429, 500, 502, 503, 504];

    // SMTP 4xx response codes indicate temporary failures (retriable)
    // 5xx codes indicate permanent failures (not retriable)
    private const int SmtpTransientMin = 400;
    private const int SmtpTransientMax = 499;

    // Transient error message fragments (case-insensitive)
    private static readonly string[] TransientMessageFragments =
    [
        "timeout",
        "timed out",
        "connection refused",
        "connection reset",
        "temporarily unavailable",
        "rate limit",
        "too many requests",
        "service unavailable",
        "try again",
        "transient",
        "socket",
        "network",
        "io error"
    ];

    // Permanent error message fragments (case-insensitive)
    private static readonly string[] PermanentMessageFragments =
    [
        "invalid email",
        "invalid address",
        "authentication failed",
        "authentication failure",
        "invalid phone",
        "not in e.164",
        "template error",
        "template rendering",
        "attachment"
    ];

    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        return exception switch
        {
            // SMTP exceptions with explicit transient flag
            SmtpDispatchException smtpEx => smtpEx.IsTransient,

            // SMS exceptions with explicit transient flag
            SmsDispatchException smsEx => smsEx.IsTransient,

            // Attachment validation is permanent
            AttachmentValidationException => false,

            // Invalid phone number is permanent
            InvalidPhoneNumberException => false,

            // Template rendering errors are permanent
            TemplateRenderException => false,

            // Socket/network errors are transient
            SocketException => true,

            // IO errors are generally transient
            IOException => true,

            // Cancellations are not retried (they are intentional)
            OperationCanceledException => false,

            // Domain exceptions are permanent (invariant violations)
            DomainException => false,

            // Unknown exceptions — treat as transient to attempt recovery
            _ => true
        };
    }

    /// <inheritdoc />
    public bool IsTransientMessage(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        var lower = errorMessage.ToLowerInvariant();

        // Check permanent fragments first (higher specificity)
        foreach (var fragment in PermanentMessageFragments)
        {
            if (lower.Contains(fragment))
                return false;
        }

        // Check transient fragments
        foreach (var fragment in TransientMessageFragments)
        {
            if (lower.Contains(fragment))
                return true;
        }

        return false;
    }
}
