using System.Text.RegularExpressions;

namespace CampaignEngine.Infrastructure.Logging;

/// <summary>
/// Provides PII (Personally Identifiable Information) masking utilities for log messages.
/// Call these helpers before including user-supplied data in structured log properties.
///
/// Business rule: PII must never appear in plain text in logs.
/// Mask email addresses, phone numbers, and other identifiers before logging.
/// </summary>
public static class PiiMasker
{
    // Regex patterns for common PII
    private static readonly Regex EmailPattern =
        new(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhonePattern =
        new(@"\+?[0-9\s\-\(\)]{7,}",
            RegexOptions.Compiled);

    /// <summary>
    /// Masks an email address, keeping the domain visible.
    /// Example: "john.doe@example.com" → "j***@example.com"
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "***@[masked]";

        var local = email[..atIndex];
        var domain = email[atIndex..]; // includes the @

        var maskedLocal = local.Length > 1
            ? $"{local[0]}***"
            : "***";

        return $"{maskedLocal}{domain}";
    }

    /// <summary>
    /// Masks a phone number, keeping only the last 4 digits.
    /// Example: "+33 6 12 34 56 78" → "***6789" (last 4 digits)
    /// </summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "[empty]";

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "***";

        return $"***{digits[^4..]}";
    }

    /// <summary>
    /// Scans a free-form string and replaces email addresses with masked versions.
    /// Use this when logging messages that may contain PII embedded in text.
    /// </summary>
    public static string MaskEmailsInText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return EmailPattern.Replace(text, match => MaskEmail(match.Value));
    }

    /// <summary>
    /// Returns a short, safe identifier for use in logs when only a reference is needed.
    /// Uses the first 8 characters of a GUID string.
    /// </summary>
    public static string SafeId(Guid id) => id.ToString("N")[..8];
}
