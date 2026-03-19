using System.Globalization;
using Scriban.Runtime;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Custom Scriban functions registered in the rendering context.
///
/// Available functions in templates:
///   format_date(value, format)     - Format a date/DateTime value using a .NET format string.
///   format_currency(value, symbol) - Format a decimal number as currency with an optional symbol.
///
/// Usage in templates:
///   {{ format_date invoice_date "dd/MM/yyyy" }}
///   {{ format_currency total_amount "€" }}
///
/// Implementation note:
///   Scriban requires functions to be registered as members of a ScriptObject subclass.
///   Static methods with snake_case names are auto-imported by ScriptObject.Import().
///   Method naming: Scriban converts PascalCase -> snake_case automatically on import.
/// </summary>
public sealed class TemplateCustomFunctions : ScriptObject
{
    /// <summary>
    /// Creates a TemplateCustomFunctions instance with all custom functions imported.
    /// Use <see cref="CreateAndRegister"/> to obtain a ready-to-use instance.
    /// </summary>
    public TemplateCustomFunctions()
    {
        // ScriptObject.Import(Type) imports all public static methods of the given type.
        // PascalCase method names are converted to snake_case:
        //   FormatDate      -> format_date
        //   FormatCurrency  -> format_currency
        this.Import(typeof(TemplateCustomFunctions));
    }

    /// <summary>
    /// Factory method: creates a TemplateCustomFunctions instance ready to push into a Scriban context.
    /// </summary>
    public static TemplateCustomFunctions CreateAndRegister() => new();

    // ----------------------------------------------------------------
    // format_date
    // ----------------------------------------------------------------

    /// <summary>
    /// Formats a date value using the given .NET format string.
    /// Imported into Scriban as <c>format_date</c>.
    ///
    /// Supported input types: DateTime, DateTimeOffset, string (ISO 8601 parsed).
    /// If the value is null or unparseable, returns an empty string.
    ///
    /// Template usage:
    ///   {{ format_date birth_date "dd/MM/yyyy" }}         -> 19/03/1990
    ///   {{ format_date invoice_date "MMMM d, yyyy" }}     -> March 19, 2026
    /// </summary>
    public static string FormatDate(object? value, string format)
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
            string s when DateTime.TryParse(s, out var parsed) => parsed,
            _ => null
        };

        return dateTime.HasValue ? dateTime.Value.ToString(format) : string.Empty;
    }

    // ----------------------------------------------------------------
    // format_currency
    // ----------------------------------------------------------------

    /// <summary>
    /// Formats a numeric value as currency.
    /// Imported into Scriban as <c>format_currency</c>.
    ///
    /// The symbol parameter is prepended to the formatted number.
    /// If symbol is omitted or empty, no prefix is added.
    /// The number is always formatted with 2 decimal places and thousands separator.
    ///
    /// Template usage:
    ///   {{ format_currency total "€" }}    -> €1,234.56
    ///   {{ format_currency price "$" }}    -> $9.99
    ///   {{ format_currency amount "" }}    -> 1,234.56
    /// </summary>
    public static string FormatCurrency(object? value, string symbol)
    {
        if (value is null)
            return string.Empty;

        decimal amount;
        try
        {
            amount = Convert.ToDecimal(value);
        }
        catch
        {
            return string.Empty;
        }

        // Use InvariantCulture to produce consistent output regardless of server locale:
        // N2 with InvariantCulture uses "." as decimal separator and "," as thousands separator.
        var formatted = amount.ToString("N2", CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(symbol) ? formatted : symbol + formatted;
    }
}
