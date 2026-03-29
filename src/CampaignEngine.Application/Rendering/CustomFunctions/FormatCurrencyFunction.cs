using System.Globalization;

namespace CampaignEngine.Application.Rendering.CustomFunctions;

/// <summary>
/// Pure implementation of the <c>format_currency</c> custom template function.
///
/// Available in Email and SMS Scriban templates as <c>format_currency</c>.
/// NOT available in DOCX/Letter templates (DOCX renderer uses plain-text
/// <c>{{ }}</c> placeholder substitution — custom Scriban functions are
/// Email/SMS-only, per F-305).
///
/// Usage in templates:
///   {{ format_currency total "€" }}    -> €1,234.56
///   {{ format_currency price "$" }}    -> $9.99
///   {{ format_currency amount "" }}    -> 1,234.56
///
/// Business rules (BR-019-02):
///   1. Currency symbol is a prefix (e.g., €100.00).
///   2. Always formats with 2 decimal places.
///   3. Uses invariant culture: dot as decimal separator, comma as thousands separator.
///   4. Null or non-numeric values return empty string.
/// </summary>
public static class FormatCurrencyFunction
{
    /// <summary>
    /// Formats a numeric value as currency with an optional prefix symbol.
    ///
    /// The symbol is prepended directly without a space (e.g. "€1,234.56").
    /// If <paramref name="symbol"/> is null or empty, only the number is returned.
    /// Non-numeric or null <paramref name="value"/> returns empty string.
    /// </summary>
    /// <param name="value">The numeric amount to format (decimal, double, int, etc.).</param>
    /// <param name="symbol">Currency symbol to prepend (e.g. "€", "$"). Use "" for none.</param>
    /// <returns>The formatted currency string, or empty string for null/invalid input.</returns>
    public static string Execute(object? value, string? symbol)
    {
        if (value is null)
            return string.Empty;

        decimal amount;
        try
        {
            amount = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return string.Empty;
        }

        // N2 with InvariantCulture: dot as decimal separator, comma as thousands separator.
        var formatted = amount.ToString("N2", CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(symbol) ? formatted : symbol + formatted;
    }
}
