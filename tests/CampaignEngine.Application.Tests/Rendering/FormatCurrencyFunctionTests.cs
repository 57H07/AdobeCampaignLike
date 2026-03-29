using CampaignEngine.Application.Rendering.CustomFunctions;

namespace CampaignEngine.Application.Tests.Rendering;

/// <summary>
/// TASK-019-05: Unit tests for FormatCurrencyFunction with various symbols.
/// Verifies pure function logic independently of the Scriban rendering engine.
/// </summary>
public class FormatCurrencyFunctionTests
{
    // ----------------------------------------------------------------
    // AC: format_currency function: {{ format_currency amount "€" }}
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_DecimalWithEuroSymbol_PrefixesEuro()
    {
        var result = FormatCurrencyFunction.Execute(1234.56m, "€");

        result.Should().Be("€1,234.56");
    }

    [Fact]
    public void Execute_DecimalWithDollarSymbol_PrefixesDollar()
    {
        var result = FormatCurrencyFunction.Execute(9.99m, "$");

        result.Should().Be("$9.99");
    }

    [Fact]
    public void Execute_DecimalWithPoundSymbol_PrefixesPound()
    {
        var result = FormatCurrencyFunction.Execute(500.00m, "£");

        result.Should().Be("£500.00");
    }

    [Fact]
    public void Execute_DecimalWithYenSymbol_PrefixesYen()
    {
        var result = FormatCurrencyFunction.Execute(10000.00m, "¥");

        result.Should().Be("¥10,000.00");
    }

    // ----------------------------------------------------------------
    // No symbol (empty string)
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_EmptySymbol_ReturnsNumberOnly()
    {
        var result = FormatCurrencyFunction.Execute(1234.56m, "");

        result.Should().Be("1,234.56");
    }

    [Fact]
    public void Execute_NullSymbol_ReturnsNumberOnly()
    {
        var result = FormatCurrencyFunction.Execute(500.00m, null);

        result.Should().Be("500.00");
    }

    // ----------------------------------------------------------------
    // Formatting rules (BR: 2 decimal places, invariant culture)
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_IntegerValue_AlwaysTwoDecimalPlaces()
    {
        var result = FormatCurrencyFunction.Execute(100, "€");

        result.Should().Be("€100.00");
    }

    [Fact]
    public void Execute_DoubleValue_FormatsCorrectly()
    {
        var result = FormatCurrencyFunction.Execute(99.9, "€");

        result.Should().Be("€99.90");
    }

    [Fact]
    public void Execute_LargeAmount_UsesThousandsSeparator()
    {
        var result = FormatCurrencyFunction.Execute(1000000.00m, "€");

        result.Should().Be("€1,000,000.00");
    }

    [Fact]
    public void Execute_ZeroAmount_FormatsAsZero()
    {
        var result = FormatCurrencyFunction.Execute(0m, "€");

        result.Should().Be("€0.00");
    }

    [Fact]
    public void Execute_NegativeAmount_FormatsWithSign()
    {
        var result = FormatCurrencyFunction.Execute(-42.50m, "€");

        result.Should().Be("€-42.50");
    }

    // ----------------------------------------------------------------
    // Null and edge cases
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_NullValue_ReturnsEmptyString()
    {
        var result = FormatCurrencyFunction.Execute(null, "€");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Execute_NonNumericString_ReturnsEmptyString()
    {
        var result = FormatCurrencyFunction.Execute("not-a-number", "€");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Execute_NonNumericObject_ReturnsEmptyString()
    {
        var result = FormatCurrencyFunction.Execute(new object(), "€");

        result.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Multiple symbols (BR: symbol is a prefix)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("€", "€1,234.56")]
    [InlineData("$", "$1,234.56")]
    [InlineData("£", "£1,234.56")]
    [InlineData("CHF", "CHF1,234.56")]
    [InlineData("", "1,234.56")]
    public void Execute_VariousSymbols_PrefixesSymbolCorrectly(string symbol, string expected)
    {
        var result = FormatCurrencyFunction.Execute(1234.56m, symbol);

        result.Should().Be(expected);
    }

    // ----------------------------------------------------------------
    // Invariant culture (dot as decimal separator regardless of server locale)
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_UsesInvariantCulture_DotAsDecimalSeparator()
    {
        var result = FormatCurrencyFunction.Execute(1.5m, "€");

        // Must use dot (.) not comma (,) as decimal separator (InvariantCulture)
        result.Should().Contain(".");
        result.Should().Be("€1.50");
    }
}
