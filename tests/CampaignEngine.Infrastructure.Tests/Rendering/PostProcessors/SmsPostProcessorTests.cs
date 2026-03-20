using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Rendering.PostProcessors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampaignEngine.Infrastructure.Tests.Rendering.PostProcessors;

/// <summary>
/// Unit tests for SmsPostProcessor.
/// Covers: HTML stripping, whitespace normalization, truncation at 160 chars,
/// whole-word truncation, custom max length, entity decoding, edge cases.
/// </summary>
public class SmsPostProcessorTests
{
    private readonly SmsPostProcessor _processor;

    public SmsPostProcessorTests()
    {
        _processor = new SmsPostProcessor(NullLogger<SmsPostProcessor>.Instance);
    }

    // ----------------------------------------------------------------
    // Channel identifier
    // ----------------------------------------------------------------

    [Fact]
    public void Channel_ReturnsSms()
    {
        _processor.Channel.Should().Be(ChannelType.Sms);
    }

    // ----------------------------------------------------------------
    // TASK-013-04: HTML stripping
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_PlainTextInput_ReturnsUnchanged()
    {
        var result = await _processor.ProcessAsync("Hello World", context: null);

        result.TextContent.Should().Be("Hello World");
        result.ContentType.Should().Be("text/plain");
        result.IsBinary.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_SimpleHtmlTags_StripsAllTags()
    {
        var html = "<p><strong>Hello</strong> <em>World</em></p>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().Be("Hello World");
    }

    [Fact]
    public async Task ProcessAsync_NestedHtmlWithLinks_StripsAllMarkup()
    {
        var html = "<div><p>Click <a href='https://example.com'>here</a> for info.</p></div>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().Be("Click here for info.");
    }

    [Fact]
    public async Task ProcessAsync_ScriptTag_RemovesScriptContent()
    {
        // Script content must be removed, not included in text
        var html = "<p>Hello</p><script>alert('xss');</script><p>World</p>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().NotContain("alert");
        result.TextContent.Should().Contain("Hello");
        result.TextContent.Should().Contain("World");
    }

    [Fact]
    public async Task ProcessAsync_StyleTag_RemovesStyleContent()
    {
        var html = "<style>body { color: red; }</style><p>Message</p>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().NotContain("color");
        result.TextContent.Should().Be("Message");
    }

    // ----------------------------------------------------------------
    // HTML entity decoding
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_HtmlEntities_DecodesEntities()
    {
        var html = "<p>Hello &amp; World &lt;3 &quot;test&quot;</p>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().Be("Hello & World <3 \"test\"");
    }

    [Fact]
    public async Task ProcessAsync_NonBreakingSpace_NormalizesWhitespace()
    {
        var html = "<p>Hello&nbsp;World</p>";

        var result = await _processor.ProcessAsync(html);

        // &nbsp; decodes to non-breaking space U+00A0, then gets normalized
        result.TextContent.Should().NotBeNullOrEmpty();
        result.TextContent.Should().Contain("Hello");
        result.TextContent.Should().Contain("World");
    }

    // ----------------------------------------------------------------
    // Whitespace normalization
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_MultipleNewlinesAndSpaces_NormalizesToSingleSpace()
    {
        var html = "<p>Hello</p><p>World</p>";

        var result = await _processor.ProcessAsync(html);

        // Multiple whitespace between paragraphs collapsed to single space
        result.TextContent.Should().NotContain("  "); // No double spaces
    }

    // ----------------------------------------------------------------
    // TASK-013-04 + BR-3: Truncation at 160 characters, whole-word preservation
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ContentUnder160Chars_ReturnsFullContent()
    {
        var shortText = "This is a short message under 160 characters.";
        var html = $"<p>{shortText}</p>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().Be(shortText);
    }

    [Fact]
    public async Task ProcessAsync_ContentOver160Chars_TruncatesTo160()
    {
        // 200-character message
        var longText = new string('A', 160) + " overflow that should be cut";
        var html = $"<p>{longText}</p>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent!.Length.Should().BeLessOrEqualTo(160);
    }

    [Fact]
    public async Task ProcessAsync_ContentExactly160Chars_ReturnsFullContent()
    {
        var text = new string('X', 160);
        var html = $"<p>{text}</p>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().Be(text);
        result.TextContent!.Length.Should().Be(160);
    }

    [Fact]
    public async Task ProcessAsync_TruncationPreservesWholeWords()
    {
        // Build: "A"*154 + " word" = 159 chars, then " overflow content" appended
        // At maxLength=160: candidate = "A"*154 + " word " (160 chars)
        // text[160] is 'o' (start of "overflow") — not a space → backup to last space
        // Last space in candidate at position 159 → result = "A"*154 + " word" (159 chars)
        var prefix = new string('A', 154) + " word";  // 159 chars
        var suffix = " overflow content";              // starts at index 159
        var html = $"<p>{prefix}{suffix}</p>";

        var result = await _processor.ProcessAsync(html);

        // Should preserve "word" and cut before "overflow"
        result.TextContent.Should().EndWith("word");
        result.TextContent!.Length.Should().BeLessOrEqualTo(160);
        result.TextContent.Should().NotContain("overflow");
    }

    [Fact]
    public async Task ProcessAsync_NoWordBoundaryBeforeLimit_HardTruncates()
    {
        // 200 chars with no spaces
        var text = new string('A', 200);

        var result = await _processor.ProcessAsync($"<p>{text}</p>");

        result.TextContent!.Length.Should().Be(160);
    }

    // ----------------------------------------------------------------
    // Custom max length via context
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CustomMaxLength_UsesContextLimit()
    {
        var text = new string('A', 300);
        var context = new PostProcessingContext { SmsMaxLength = 80 };

        var result = await _processor.ProcessAsync($"<p>{text}</p>", context);

        result.TextContent!.Length.Should().BeLessOrEqualTo(80);
    }

    // ----------------------------------------------------------------
    // Edge cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_EmptyString_ReturnsEmpty()
    {
        var result = await _processor.ProcessAsync(string.Empty);

        result.TextContent.Should().BeEmpty();
        result.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task ProcessAsync_WhitespaceOnly_ReturnsEmpty()
    {
        var result = await _processor.ProcessAsync("   \n\t  ");

        result.TextContent.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // TruncateWholeWords static helper (internal method)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("Hello World", 20, "Hello World")]   // Under limit — unchanged
    [InlineData("Hello World", 11, "Hello World")]   // Exact limit — unchanged
    [InlineData("Hello World!", 11, "Hello")]         // Mid-word cut: "World!" continues past limit → backup to "Hello"
    [InlineData("HelloWorld!", 5, "Hello")]           // Hard truncate — no space in first 5 chars
    [InlineData("Hello World", 6, "Hello")]           // text[6]='W' (non-space) → backup to "Hello" at lastSpace=5
    [InlineData("Hello World", 5, "Hello")]           // text[5]=' ' — word boundary → return "Hello"
    public void TruncateWholeWords_ReturnsExpectedResult(
        string input, int maxLength, string expected)
    {
        var result = SmsPostProcessor.TruncateWholeWords(input, maxLength);

        result.Should().Be(expected);
    }
}
