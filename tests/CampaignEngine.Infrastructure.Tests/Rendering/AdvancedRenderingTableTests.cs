using CampaignEngine.Application.Models;
using CampaignEngine.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// TASK-012-05: Unit tests for table rendering.
/// Verifies that the ScribanTemplateRenderer produces correct HTML table output
/// when iterating over arrays of objects.
/// </summary>
public class AdvancedRenderingTableTests
{
    private readonly ScribanTemplateRenderer _renderer;

    public AdvancedRenderingTableTests()
    {
        _renderer = new ScribanTemplateRenderer(NullLogger<ScribanTemplateRenderer>.Instance);
    }

    // ----------------------------------------------------------------
    // Basic table iteration
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_TableWithRows_GeneratesHtmlTableRows()
    {
        // Business rule: Table syntax iterates over array data to generate HTML table rows.
        var template = """
            <table>
            {{ for row in order_lines }}
            <tr><td>{{ row.product }}</td><td>{{ row.quantity }}</td></tr>
            {{ end }}
            </table>
            """;

        var data = new Dictionary<string, object?>
        {
            ["order_lines"] = new List<object?>
            {
                new Dictionary<string, object?> { ["product"] = "Widget A", ["quantity"] = 2 },
                new Dictionary<string, object?> { ["product"] = "Widget B", ["quantity"] = 1 }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<tr><td>Widget A</td><td>2</td></tr>");
        result.Should().Contain("<tr><td>Widget B</td><td>1</td></tr>");
    }

    [Fact]
    public async Task RenderAsync_TableWithThreeColumns_RendersAllColumns()
    {
        var template = """
            {{ for row in products }}
            <tr><td>{{ row.name }}</td><td>{{ row.price }}</td><td>{{ row.stock }}</td></tr>
            {{ end }}
            """;

        var data = new Dictionary<string, object?>
        {
            ["products"] = new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "Laptop", ["price"] = 999.99m, ["stock"] = 42 },
                new Dictionary<string, object?> { ["name"] = "Mouse", ["price"] = 29.99m, ["stock"] = 200 }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("Laptop").And.Contain("999.99").And.Contain("42");
        result.Should().Contain("Mouse").And.Contain("29.99").And.Contain("200");
    }

    // ----------------------------------------------------------------
    // Empty collection — business rule BR-4: empty tables render nothing
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_EmptyTableArray_RendersNothingInsideLoop()
    {
        // Business rule: Empty collections render nothing (no placeholder text).
        var template = """
            <table>
            {{ for row in items }}
            <tr><td>{{ row.name }}</td></tr>
            {{ end }}
            </table>
            """;

        var data = new Dictionary<string, object?>
        {
            ["items"] = new List<object?>()
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<table>");
        result.Should().Contain("</table>");
        result.Should().NotContain("<tr>");
    }

    [Fact]
    public async Task RenderAsync_NullTableArray_RendersNothingInsideLoop()
    {
        var template = """
            {{ for row in items }}
            <tr><td>{{ row.name }}</td></tr>
            {{ end }}
            """;

        var data = new Dictionary<string, object?>
        {
            ["items"] = null
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().NotContain("<tr>");
    }

    // ----------------------------------------------------------------
    // Row index access
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ForLoopWithIndex_RendersZeroBasedRowIndex()
    {
        var template = "{{ for row in rows }}{{ for.index }}:{{ row.val }} {{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["rows"] = new List<object?>
            {
                new Dictionary<string, object?> { ["val"] = "A" },
                new Dictionary<string, object?> { ["val"] = "B" },
                new Dictionary<string, object?> { ["val"] = "C" }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("0:A").And.Contain("1:B").And.Contain("2:C");
    }

    // ----------------------------------------------------------------
    // Missing field in row object
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_RowMissingField_RendersEmptyStringForMissingField()
    {
        var template = "{{ for row in rows }}<td>{{ row.name }}</td><td>{{ row.optional }}</td>{{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["rows"] = new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "Alice" } // no "optional" field
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<td>Alice</td><td></td>");
    }

    // ----------------------------------------------------------------
    // HTML encoding in table cells
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_TableCellWithHtmlChars_IsHtmlEncoded()
    {
        var template = "{{ for row in rows }}<td>{{ row.value }}</td>{{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["rows"] = new List<object?>
            {
                new Dictionary<string, object?> { ["value"] = "<script>alert('xss')</script>" }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().NotContain("<script>");
        result.Should().Contain("&lt;script&gt;");
    }
}
