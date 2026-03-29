using CampaignEngine.Application.Rendering.CustomFunctions;

namespace CampaignEngine.Application.Tests.Rendering;

/// <summary>
/// TASK-019-04: Unit tests for FormatDateFunction with various formats.
/// Verifies pure function logic independently of the Scriban rendering engine.
/// </summary>
public class FormatDateFunctionTests
{
    // ----------------------------------------------------------------
    // AC: format_date function: {{ format_date invoiceDate "dd/MM/yyyy" }}
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_DateTime_DdMmYyyyFormat_ReturnsFormattedDate()
    {
        var result = FormatDateFunction.Execute(new DateTime(2026, 3, 19), "dd/MM/yyyy");

        result.Should().Be("19/03/2026");
    }

    [Fact]
    public void Execute_DateTime_IsoFormat_ReturnsFormattedDate()
    {
        var result = FormatDateFunction.Execute(new DateTime(2026, 3, 19), "yyyy-MM-dd");

        result.Should().Be("2026-03-19");
    }

    [Fact]
    public void Execute_DateTime_LongDateFormat_ReturnsMonthName()
    {
        var result = FormatDateFunction.Execute(new DateTime(2026, 3, 19), "MMMM d, yyyy");

        result.Should().Be("March 19, 2026");
    }

    [Fact]
    public void Execute_DateTime_DateTimeFormat_IncludesTime()
    {
        var result = FormatDateFunction.Execute(new DateTime(2026, 3, 19, 14, 30, 0), "yyyy-MM-dd HH:mm");

        result.Should().Be("2026-03-19 14:30");
    }

    [Fact]
    public void Execute_DateTime_ShortAbbreviatedFormat_ReturnsAbbreviated()
    {
        var result = FormatDateFunction.Execute(new DateTime(2026, 3, 19), "dd MMM yy");

        result.Should().Be("19 Mar 26");
    }

    // ----------------------------------------------------------------
    // DateTimeOffset input
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_DateTimeOffset_ReturnsFormattedDate()
    {
        var dto = new DateTimeOffset(new DateTime(2026, 6, 1), TimeSpan.FromHours(2));

        var result = FormatDateFunction.Execute(dto, "dd/MM/yyyy");

        result.Should().Be("01/06/2026");
    }

    // ----------------------------------------------------------------
    // DateOnly input
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_DateOnly_ReturnsFormattedDate()
    {
        var dateOnly = new DateOnly(2026, 12, 25);

        var result = FormatDateFunction.Execute(dateOnly, "dd/MM/yyyy");

        result.Should().Be("25/12/2026");
    }

    // ----------------------------------------------------------------
    // String input (ISO 8601 parsing)
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_IsoDateString_ParsesAndFormats()
    {
        var result = FormatDateFunction.Execute("1990-06-15", "yyyy-MM-dd");

        result.Should().Be("1990-06-15");
    }

    [Fact]
    public void Execute_IsoDateString_FormatsWithDifferentFormat()
    {
        var result = FormatDateFunction.Execute("2026-03-19", "dd/MM/yyyy");

        result.Should().Be("19/03/2026");
    }

    // ----------------------------------------------------------------
    // Null and edge cases
    // ----------------------------------------------------------------

    [Fact]
    public void Execute_NullValue_ReturnsEmptyString()
    {
        var result = FormatDateFunction.Execute(null, "dd/MM/yyyy");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Execute_UnparseableString_ReturnsEmptyString()
    {
        var result = FormatDateFunction.Execute("not-a-date", "dd/MM/yyyy");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Execute_UnsupportedType_ReturnsEmptyString()
    {
        var result = FormatDateFunction.Execute(12345, "dd/MM/yyyy");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Execute_EmptyFormatString_UsesDefaultFormat()
    {
        // Empty format falls back to "d" (short date pattern)
        var result = FormatDateFunction.Execute(new DateTime(2026, 3, 19), "");

        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Execute_NullFormat_UsesDefaultFormat()
    {
        var result = FormatDateFunction.Execute(new DateTime(2026, 3, 19), null);

        result.Should().NotBeEmpty();
    }

    // ----------------------------------------------------------------
    // Multiple formats (BR: format strings use .NET standard format specifiers)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("dd/MM/yyyy", "19/03/2026")]
    [InlineData("MM/dd/yyyy", "03/19/2026")]
    [InlineData("yyyy-MM-dd", "2026-03-19")]
    [InlineData("d MMMM yyyy", "19 March 2026")]
    public void Execute_VariousFormats_ReturnsExpectedOutput(string format, string expected)
    {
        var date = new DateTime(2026, 3, 19);

        var result = FormatDateFunction.Execute(date, format);

        result.Should().Be(expected);
    }
}
