namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service for validating and normalizing email addresses for CC/BCC configuration.
///
/// US-029: Email validation for all CC and BCC addresses.
///
/// Business rules:
///   BR-1: Invalid emails are logged but do not fail the send.
///   BR-2: Max 10 CC recipients per send (enforced by CcResolutionService).
///   BR-3: Addresses are normalized to lowercase before deduplication.
/// </summary>
public interface IEmailValidationService
{
    /// <summary>
    /// Returns true if the given email address is syntactically valid.
    /// Uses RFC 5321-compatible parsing (MimeKit MailboxAddress).
    /// </summary>
    bool IsValid(string? email);

    /// <summary>
    /// Validates a collection of email addresses and returns only the valid ones.
    /// Invalid addresses are logged; they do not throw.
    /// </summary>
    /// <param name="addresses">Raw email address strings to validate.</param>
    /// <param name="context">Descriptive context for log messages (e.g., "StaticCC", "DynamicCC").</param>
    /// <returns>List of valid email addresses (preserving original casing).</returns>
    IReadOnlyList<string> FilterValid(IEnumerable<string> addresses, string context);
}
