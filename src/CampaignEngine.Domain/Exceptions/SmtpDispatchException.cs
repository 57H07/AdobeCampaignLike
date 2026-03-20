namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Thrown when an SMTP send operation fails.
/// TASK-019-05: SMTP error categorization (transient vs permanent).
///
/// Transient errors (retriable): connection timeout, server unavailable, rate limiting.
/// Permanent errors (not retriable): invalid recipient address, message rejected.
/// </summary>
public class SmtpDispatchException : DomainException
{
    /// <summary>
    /// Whether the failure is transient and should be retried.
    /// Transient: SMTP connection failure, timeout, server temporarily unavailable (4xx responses).
    /// Permanent: invalid recipient (5xx reject), authentication failure, malformed message.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>Optional SMTP response code (e.g. 421, 550).</summary>
    public int? SmtpStatusCode { get; }

    public SmtpDispatchException(string message, bool isTransient, int? smtpStatusCode = null)
        : base(message)
    {
        IsTransient = isTransient;
        SmtpStatusCode = smtpStatusCode;
    }

    public SmtpDispatchException(string message, bool isTransient, Exception innerException, int? smtpStatusCode = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
        SmtpStatusCode = smtpStatusCode;
    }
}

/// <summary>
/// Thrown when an attachment fails validation (size or type whitelist).
/// TASK-019-03: Attachment handling validation.
/// This is a permanent failure — retrying will not help.
/// </summary>
public class AttachmentValidationException : DomainException
{
    public string AttachmentFileName { get; }

    public AttachmentValidationException(string message, string fileName)
        : base(message)
    {
        AttachmentFileName = fileName;
    }
}
