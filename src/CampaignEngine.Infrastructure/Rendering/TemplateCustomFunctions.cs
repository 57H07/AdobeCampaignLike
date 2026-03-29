using CampaignEngine.Application.Rendering.CustomFunctions;
using Scriban.Runtime;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Custom Scriban functions registered in the rendering context for Email and SMS templates.
///
/// Available functions in templates:
///   format_date(value, format)     - Format a date/DateTime value using a .NET format string.
///   format_currency(value, symbol) - Format a decimal number as currency with an optional symbol.
///
/// Usage in templates:
///   {{ format_date invoice_date "dd/MM/yyyy" }}
///   {{ format_currency total_amount "€" }}
///
/// NOTE: These functions are registered only for the Scriban renderer (Email/SMS channels).
/// They are NOT available in the DOCX renderer used for Letter channel templates, which uses
/// plain-text {{ }} placeholder substitution via DocumentFormat.OpenXml (see F-305).
///
/// Implementation note:
///   Scriban requires functions to be registered as members of a ScriptObject subclass.
///   Static methods with snake_case names are auto-imported by ScriptObject.Import().
///   Method naming: Scriban converts PascalCase -> snake_case automatically on import.
///   Pure function logic lives in Application/Rendering/CustomFunctions/ (no Scriban dep).
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
    /// Called from <see cref="ScribanTemplateRenderer.BuildScribanContext"/> at render time.
    /// </summary>
    public static TemplateCustomFunctions CreateAndRegister() => new();

    // ----------------------------------------------------------------
    // format_date
    // ----------------------------------------------------------------

    /// <summary>
    /// Formats a date value using the given .NET format string.
    /// Imported into Scriban as <c>format_date</c>.
    ///
    /// Delegates to <see cref="FormatDateFunction.Execute"/> (Application layer).
    ///
    /// Template usage:
    ///   {{ format_date birth_date "dd/MM/yyyy" }}         -> 19/03/1990
    ///   {{ format_date invoice_date "MMMM d, yyyy" }}     -> March 19, 2026
    /// </summary>
    public static string FormatDate(object? value, string format)
        => FormatDateFunction.Execute(value, format);

    // ----------------------------------------------------------------
    // format_currency
    // ----------------------------------------------------------------

    /// <summary>
    /// Formats a numeric value as currency.
    /// Imported into Scriban as <c>format_currency</c>.
    ///
    /// Delegates to <see cref="FormatCurrencyFunction.Execute"/> (Application layer).
    ///
    /// Template usage:
    ///   {{ format_currency total "€" }}    -> €1,234.56
    ///   {{ format_currency price "$" }}    -> $9.99
    ///   {{ format_currency amount "" }}    -> 1,234.56
    /// </summary>
    public static string FormatCurrency(object? value, string symbol)
        => FormatCurrencyFunction.Execute(value, symbol);
}
