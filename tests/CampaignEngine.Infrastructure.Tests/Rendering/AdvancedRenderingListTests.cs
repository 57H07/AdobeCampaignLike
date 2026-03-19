using CampaignEngine.Application.Models;
using CampaignEngine.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// TASK-012-06: Unit tests for list rendering.
/// Verifies that the ScribanTemplateRenderer correctly iterates scalar arrays
/// and generates bulleted or numbered HTML list items.
/// </summary>
public class AdvancedRenderingListTests
{
    private readonly ScribanTemplateRenderer _renderer;

    public AdvancedRenderingListTests()
    {
        _renderer = new ScribanTemplateRenderer(NullLogger<ScribanTemplateRenderer>.Instance);
    }

    // ----------------------------------------------------------------
    // Basic bulleted list
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_BulletedList_GeneratesLiElements()
    {
        // Business rule: List syntax iterates and generates bulleted/numbered lists.
        var template = """
            <ul>
            {{ for item in features }}
            <li>{{ item }}</li>
            {{ end }}
            </ul>
            """;

        var data = new Dictionary<string, object?>
        {
            ["features"] = new List<object?> { "Fast delivery", "Free returns", "24/7 support" }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<li>Fast delivery</li>");
        result.Should().Contain("<li>Free returns</li>");
        result.Should().Contain("<li>24/7 support</li>");
    }

    [Fact]
    public async Task RenderAsync_NumberedList_GeneratesOrderedLiElements()
    {
        var template = """
            <ol>
            {{ for item in steps }}
            <li>{{ item }}</li>
            {{ end }}
            </ol>
            """;

        var data = new Dictionary<string, object?>
        {
            ["steps"] = new List<object?> { "Open app", "Select product", "Checkout" }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<ol>").And.Contain("</ol>");
        result.Should().Contain("<li>Open app</li>");
        result.Should().Contain("<li>Select product</li>");
        result.Should().Contain("<li>Checkout</li>");
    }

    // ----------------------------------------------------------------
    // Empty list — business rule BR-4
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_EmptyList_RendersNothingInsideLoop()
    {
        // Business rule: Empty collections render nothing (no placeholder text).
        var template = """
            <ul>
            {{ for item in items }}
            <li>{{ item }}</li>
            {{ end }}
            </ul>
            """;

        var data = new Dictionary<string, object?>
        {
            ["items"] = new List<object?>()
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<ul>").And.Contain("</ul>");
        result.Should().NotContain("<li>");
    }

    [Fact]
    public async Task RenderAsync_MissingListVariable_RendersNothingInsideLoop()
    {
        var template = "<ul>{{ for item in items }}<li>{{ item }}</li>{{ end }}</ul>";
        var data = new Dictionary<string, object?>();

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<ul></ul>");
        result.Should().NotContain("<li>");
    }

    // ----------------------------------------------------------------
    // Integer and numeric list items
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ListOfIntegers_RendersAllValues()
    {
        var template = "{{ for n in numbers }}{{ n }} {{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["numbers"] = new List<object?> { 1, 2, 3, 4, 5 }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("1 ").And.Contain("2 ").And.Contain("3 ").And.Contain("4 ").And.Contain("5 ");
    }

    // ----------------------------------------------------------------
    // First / last sentinel access
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ListWithFirstLastSentinels_AppliesCorrectly()
    {
        var template = "{{ for item in items }}{{ item }}{{ if !for.last }},{{ end }}{{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["items"] = new List<object?> { "Alpha", "Beta", "Gamma" }
        };

        var result = await _renderer.RenderAsync(template, data);

        // Items joined by comma, no trailing comma
        result.Should().Be("Alpha,Beta,Gamma");
    }

    // ----------------------------------------------------------------
    // HTML encoding in list items
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ListItemWithHtmlChars_IsHtmlEncoded()
    {
        var template = "{{ for item in items }}<li>{{ item }}</li>{{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["items"] = new List<object?> { "Tom & Jerry", "<b>Bold</b>" }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("Tom &amp; Jerry");
        result.Should().Contain("&lt;b&gt;Bold&lt;/b&gt;");
        result.Should().NotContain("<b>Bold</b>");
    }
}
