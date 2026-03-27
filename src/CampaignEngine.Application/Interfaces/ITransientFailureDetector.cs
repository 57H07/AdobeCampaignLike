namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Classifies exceptions as transient (retriable) or permanent (non-retriable).
///
/// Business rules (US-035):
///   Transient errors retried:
///     - SMTP connection timeout, server temporarily unavailable (4xx), protocol errors
///     - SMS provider rate limit (429), network timeout, temporary server error (5xx)
///     - Socket/network connectivity failures
///   Permanent errors NOT retried:
///     - Invalid email address (5xx SMTP reject)
///     - SMTP authentication failure
///     - Invalid phone number (E.164 format)
///     - Attachment validation failures
///     - Template rendering errors
/// </summary>
public interface ITransientFailureDetector
{
    /// <summary>
    /// Determines whether the given exception represents a transient failure
    /// that should be retried.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>True if the failure is transient and retry is appropriate; false for permanent failures.</returns>
    bool IsTransient(Exception exception);

    /// <summary>
    /// Determines whether a string error message indicates a transient failure.
    /// Used when the original exception is not available (e.g., dispatchers that
    /// return error strings rather than throwing).
    /// </summary>
    /// <param name="errorMessage">The error message to classify.</param>
    /// <returns>True if the error message indicates a transient failure.</returns>
    bool IsTransientMessage(string errorMessage);
}
