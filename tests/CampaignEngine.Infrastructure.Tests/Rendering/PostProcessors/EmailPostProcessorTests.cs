using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Rendering.PostProcessors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampaignEngine.Infrastructure.Tests.Rendering.PostProcessors;

/// <summary>
/// Unit tests for EmailPostProcessor.
/// Covers: CSS inlining, Outlook compatibility, content type, error handling,
/// edge cases (empty, no CSS, inline CSS passthrough).
///
/// TASK-013-09: Email CSS inlining tests (Outlook compatibility).
/// </summary>
public class EmailPostProcessorTests
{
    private readonly EmailPostProcessor _processor;

    public EmailPostProcessorTests()
    {
        _processor = new EmailPostProcessor(NullLogger<EmailPostProcessor>.Instance);
    }

    // ----------------------------------------------------------------
    // Channel identifier
    // ----------------------------------------------------------------

    [Fact]
    public void Channel_ReturnsEmail()
    {
        _processor.Channel.Should().Be(ChannelType.Email);
    }

    // ----------------------------------------------------------------
    // TASK-013-02: CSS inlining with PreMailer.Net
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_HtmlWithStyleBlock_InlinesCss()
    {
        var html = @"<html><head><style>p { color: red; }</style></head>
<body><p>Hello</p></body></html>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().NotBeNull();
        result.TextContent.Should().Contain("color");
        result.ContentType.Should().Be("text/html");
        result.IsBinary.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_HtmlWithStyleBlock_RemovesStyleElement()
    {
        // PreMailer.Net with removeStyleElements:true should remove the <style> block
        var html = @"<html><head><style>p { font-size: 14px; }</style></head>
<body><p>Message</p></body></html>";

        var result = await _processor.ProcessAsync(html);

        // The style element should be removed after inlining
        result.TextContent.Should().NotContain("<style>");
    }

    [Fact]
    public async Task ProcessAsync_HtmlWithMultipleCssRules_InlinesAllRules()
    {
        var html = @"<html><head><style>
.header { background-color: #003366; color: white; font-size: 18px; }
.body { font-family: Arial, sans-serif; color: #333; }
</style></head>
<body>
<div class='header'>CampaignEngine</div>
<div class='body'>Your message here.</div>
</body></html>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().Contain("background-color");
        result.TextContent.Should().Contain("font-family");
    }

    /// <summary>
    /// TASK-013-09: Outlook compatibility — CSS must be on element style attributes,
    /// not in a <style> block. Verifies inlined styles appear directly on elements.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_OutlookCompatibility_StylesOnElementAttributes()
    {
        var html = @"<html><head><style>td { padding: 10px; border: 1px solid #ccc; }</style></head>
<body><table><tr><td>Cell</td></tr></table></body></html>";

        var result = await _processor.ProcessAsync(html);

        // Outlook requires inline styles — CSS should appear as style attribute on <td>
        result.TextContent.Should().Contain("style=");
        result.TextContent.Should().Contain("padding");
    }

    [Fact]
    public async Task ProcessAsync_HtmlWithNoStyleBlock_ReturnsHtmlUnchanged()
    {
        var html = @"<html><body><p style=""color: red;"">Pre-inlined</p></body></html>";

        var result = await _processor.ProcessAsync(html);

        // Already inlined CSS should be preserved
        result.TextContent.Should().Contain("color: red");
        result.ContentType.Should().Be("text/html");
    }

    [Fact]
    public async Task ProcessAsync_FullEmailTemplate_PreservesTextContent()
    {
        var html = @"<html><head><style>body { font-family: Arial; }</style></head>
<body>
  <h1>Welcome, {{ name }}</h1>
  <p>Thank you for your registration.</p>
  <a href='https://example.com'>Click here</a>
</body></html>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().Contain("Welcome");
        result.TextContent.Should().Contain("Thank you for your registration");
        result.TextContent.Should().Contain("Click here");
    }

    // ----------------------------------------------------------------
    // Context propagation
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_WithContext_StillProcessesHtml()
    {
        var html = "<html><head><style>p { color: blue; }</style></head><body><p>Hi</p></body></html>";
        var context = new PostProcessingContext
        {
            CampaignId = Guid.NewGuid(),
            RecipientId = "recipient-001"
        };

        var result = await _processor.ProcessAsync(html, context);

        result.TextContent.Should().NotBeNullOrEmpty();
        result.ContentType.Should().Be("text/html");
    }

    // ----------------------------------------------------------------
    // Edge cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_EmptyString_ReturnsEmptyHtml()
    {
        var result = await _processor.ProcessAsync(string.Empty);

        result.TextContent.Should().BeEmpty();
        result.ContentType.Should().Be("text/html");
    }

    [Fact]
    public async Task ProcessAsync_WhitespaceOnly_ReturnsEmptyResult()
    {
        var result = await _processor.ProcessAsync("   \n  ");

        result.TextContent.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_HtmlFragment_ProcessesWithoutException()
    {
        // PreMailer.Net can handle both full documents and fragments
        var fragment = "<p style='font-size: 12px;'>Hello <strong>World</strong></p>";

        var act = async () => await _processor.ProcessAsync(fragment);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessAsync_ComplexTableLayout_OutlookCompatible()
    {
        // Table-based layouts are typical for Outlook-compatible emails
        var html = @"<html>
<head><style>
table { width: 100%; border-collapse: collapse; }
td { padding: 8px 16px; vertical-align: top; }
.header-cell { background-color: #003366; color: #ffffff; }
.content-cell { color: #333333; font-family: Arial, sans-serif; }
</style></head>
<body>
<table>
  <tr><td class='header-cell'>Header</td></tr>
  <tr><td class='content-cell'>Body content here</td></tr>
</table>
</body></html>";

        var result = await _processor.ProcessAsync(html);

        result.TextContent.Should().NotBeNullOrEmpty();
        // Inline styles should contain the CSS properties
        result.TextContent.Should().Contain("padding");
        result.TextContent.Should().Contain("background-color");
    }
}
