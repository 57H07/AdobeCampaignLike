using System.Globalization;

namespace CampaignEngine.Application.Rendering.CustomFunctions;

/// <summary>
/// Pure implementation of the <c>format_date</c> custom template function.
///
/// Available in Email and SMS Scriban templates as <c>format_date</c>.
/// NOT available in DOCX/Letter templates (DOCX renderer uses plain-text
/// <c>{{ }}</c> placeholder substitution — custom Scriban functions are
/// Email/SMS-only, per F-305).
///
/// Usage in templates:
///   {{ format_date invoice_date "dd/MM/yyyy" }}     -> 19/03/2026
///   {{ format_date birth_date "MMMM d, yyyy" }}    -> March 19, 1990
///   {{ format_date created_at "yyyy-MM-dd HH:mm" }} -> 2026-03-19 14:30
///
/// Business rules (BR-019-01):
///   1. Format strings use .NET standard format specifiers.
///   2. Null or unparseable values return empty string.
///   3. String inputs are parsed as ISO 8601 dates.
/// </summary>
public static class FormatDateFunction
{
    /// <summary>
    /// Formats a date value using the given .NET format string.
    ///
    /// Supported input types: <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
    /// <see cref="DateOnly"/>, or <see cref="string"/> (ISO 8601 parsed).
    /// Returns empty string for null or unparseable inputs.
    /// </summary>
    /// <param name="value">The date value to format.</param>
    /// <param name="format">A .NET date format string (e.g. "dd/MM/yyyy").</param>
    /// <returns>The formatted date string, or empty string for null/invalid input.</returns>
    public static string Execute(object? value, string? format)
    {
        if (value is null)
            return string.Empty;

        if (string.IsNullOrEmpty(format))
            format = "d";

        DateTime? dateTime = value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
            string s2 when DateTime.TryParse(s2, out var fallback) => fallback,
            _ => null
        };

        return dateTime.HasValue
            ? dateTime.Value.ToString(format, CultureInfo.InvariantCulture)
            : string.Empty;
    }
}
