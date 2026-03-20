using System.Text.RegularExpressions;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Validates phone numbers against the E.164 international format.
///
/// TASK-020-03: Phone number validation (international format).
///
/// E.164 format rules:
///   - Starts with a '+' sign
///   - Followed by the country code (1-3 digits)
///   - Then the subscriber number
///   - Total length: 7 to 15 digits (excluding the leading '+')
///
/// Examples:
///   Valid:   +12025551234 (US), +441234567890 (UK), +33123456789 (France)
///   Invalid: 12025551234 (missing +), +1 (too short), +123456789012345678 (too long)
///
/// Business rule BR-1: Phone numbers must be in E.164 format.
/// Business rule BR-4: Invalid numbers logged but don't fail campaign (controlled by caller).
/// </summary>
public static class PhoneNumberValidator
{
    /// <summary>
    /// E.164 regex: starts with '+', followed by 7 to 15 digits.
    /// Minimum 7 digits covers shortest country codes + subscriber numbers.
    /// Maximum 15 digits is the ITU-T E.164 standard limit.
    /// </summary>
    private static readonly Regex E164Regex = new(
        @"^\+[1-9]\d{6,14}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns true if the phone number is in valid E.164 format.
    /// </summary>
    public static bool IsValidE164(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return false;

        return E164Regex.IsMatch(phoneNumber);
    }

    /// <summary>
    /// Normalizes a phone number by stripping common formatting characters
    /// (spaces, dashes, parentheses) before E.164 validation.
    /// Returns the stripped number for the caller to validate.
    /// Does NOT add a '+' sign if missing.
    /// </summary>
    public static string Normalize(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return string.Empty;

        // Remove spaces, dashes, parentheses, dots
        return Regex.Replace(phoneNumber, @"[\s\-\(\)\.]", string.Empty);
    }
}
