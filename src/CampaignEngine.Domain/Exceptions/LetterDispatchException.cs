namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Thrown when a Letter channel send operation fails.
/// TASK-021-01: Letter dispatch error categorization.
///
/// Transient errors (retriable): file system I/O failure, PDF generation engine timeout,
///   UNC share temporarily unavailable.
/// Permanent errors (not retriable): empty HTML content, invalid configuration,
///   output directory not configured.
/// </summary>
public class LetterDispatchException : DomainException
{
    /// <summary>
    /// Whether the failure is transient and should be retried.
    /// Transient: I/O error, PDF engine timeout, UNC share temporarily unavailable.
    /// Permanent: missing configuration, invalid content, channel disabled.
    /// </summary>
    public bool IsTransient { get; }

    public LetterDispatchException(string message, bool isTransient)
        : base(message)
    {
        IsTransient = isTransient;
    }

    public LetterDispatchException(string message, Exception innerException, bool isTransient)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}
