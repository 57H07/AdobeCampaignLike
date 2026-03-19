using CampaignEngine.Application.Models;
using CampaignEngine.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// TASK-012-08: Integration tests for nested structures and custom functions.
/// Covers: table within conditional, list within conditional, nested objects in loops,
/// custom format_date and format_currency functions, combined scenarios.
/// </summary>
public class AdvancedRenderingIntegrationTests
{
    private readonly ScribanTemplateRenderer _renderer;

    public AdvancedRenderingIntegrationTests()
    {
        _renderer = new ScribanTemplateRenderer(NullLogger<ScribanTemplateRenderer>.Instance);
    }

    // ----------------------------------------------------------------
    // Nested: table within conditional (AC-4)
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_TableWithinConditional_ConditionTrue_RendersTable()
    {
        // Acceptance criterion: Nested structures supported (table within conditional).
        var template = """
            {{ if show_orders }}
            <table>
            {{ for row in orders }}
            <tr><td>{{ row.ref }}</td><td>{{ row.amount }}</td></tr>
            {{ end }}
            </table>
            {{ end }}
            """;

        var data = new Dictionary<string, object?>
        {
            ["show_orders"] = true,
            ["orders"] = new List<object?>
            {
                new Dictionary<string, object?> { ["ref"] = "ORD-001", ["amount"] = 99.99m },
                new Dictionary<string, object?> { ["ref"] = "ORD-002", ["amount"] = 49.50m }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<table>");
        result.Should().Contain("<tr><td>ORD-001</td><td>99.99</td></tr>");
        result.Should().Contain("<tr><td>ORD-002</td><td>49.50</td></tr>");
    }

    [Fact]
    public async Task RenderAsync_TableWithinConditional_ConditionFalse_RendersNothing()
    {
        var template = """
            {{ if show_orders }}
            <table>
            {{ for row in orders }}
            <tr><td>{{ row.ref }}</td></tr>
            {{ end }}
            </table>
            {{ end }}
            """;

        var data = new Dictionary<string, object?>
        {
            ["show_orders"] = false,
            ["orders"] = new List<object?>
            {
                new Dictionary<string, object?> { ["ref"] = "ORD-001" }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Trim().Should().BeEmpty();
        result.Should().NotContain("<table>");
    }

    // ----------------------------------------------------------------
    // Nested: list within conditional
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ListWithinConditional_OnlyRendersWhenConditionTrue()
    {
        var template = """
            {{ if has_features }}
            <ul>{{ for feature in features }}<li>{{ feature }}</li>{{ end }}</ul>
            {{ end }}
            """;

        var dataTrue = new Dictionary<string, object?>
        {
            ["has_features"] = true,
            ["features"] = new List<object?> { "Feature A", "Feature B" }
        };

        var dataFalse = new Dictionary<string, object?>
        {
            ["has_features"] = false,
            ["features"] = new List<object?> { "Feature A", "Feature B" }
        };

        var trueResult = await _renderer.RenderAsync(template, dataTrue);
        var falseResult = await _renderer.RenderAsync(template, dataFalse);

        trueResult.Should().Contain("<li>Feature A</li>").And.Contain("<li>Feature B</li>");
        falseResult.Trim().Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Nested: conditional within loop
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ConditionalWithinLoop_AppliesPerRow()
    {
        var template = """
            {{ for row in items }}
            <li>{{ row.name }}{{ if row.is_new }} (NEW){{ end }}</li>
            {{ end }}
            """;

        var data = new Dictionary<string, object?>
        {
            ["items"] = new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "Product A", ["is_new"] = true },
                new Dictionary<string, object?> { ["name"] = "Product B", ["is_new"] = false },
                new Dictionary<string, object?> { ["name"] = "Product C", ["is_new"] = true }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("Product A (NEW)");
        result.Should().NotContain("Product B (NEW)");
        result.Should().Contain("Product C (NEW)");
    }

    // ----------------------------------------------------------------
    // Custom functions: format_date
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_FormatDate_DateTime_FormatsCorrectly()
    {
        // TASK-012-04: format_date custom function
        var template = "Invoice date: {{ format_date invoice_date \"dd/MM/yyyy\" }}";

        var data = new Dictionary<string, object?>
        {
            ["invoice_date"] = new DateTime(2026, 3, 19)
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("Invoice date: 19/03/2026");
    }

    [Fact]
    public async Task RenderAsync_FormatDate_StringDate_ParsesAndFormats()
    {
        var template = "{{ format_date birth_date \"yyyy-MM-dd\" }}";

        var data = new Dictionary<string, object?>
        {
            ["birth_date"] = "1990-06-15"
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("1990-06-15");
    }

    [Fact]
    public async Task RenderAsync_FormatDate_NullValue_ReturnsEmptyString()
    {
        var template = "Date: {{ format_date expiry_date \"dd/MM/yyyy\" }}";

        var data = new Dictionary<string, object?>
        {
            ["expiry_date"] = null
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("Date: ");
    }

    [Fact]
    public async Task RenderAsync_FormatDate_InLoop_FormatsEachDate()
    {
        var template = """
            {{ for event in events }}
            {{ event.name }}: {{ format_date event.date "dd/MM/yyyy" }}
            {{ end }}
            """;

        var data = new Dictionary<string, object?>
        {
            ["events"] = new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "Launch", ["date"] = new DateTime(2026, 1, 15) },
                new Dictionary<string, object?> { ["name"] = "Review", ["date"] = new DateTime(2026, 6, 30) }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("Launch: 15/01/2026");
        result.Should().Contain("Review: 30/06/2026");
    }

    // ----------------------------------------------------------------
    // Custom functions: format_currency
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_FormatCurrency_WithSymbol_PrefixesSymbol()
    {
        // TASK-012-04: format_currency custom function
        var template = "Total: {{ format_currency total \"€\" }}";

        var data = new Dictionary<string, object?>
        {
            ["total"] = 1234.56m
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("Total: €1,234.56");
    }

    [Fact]
    public async Task RenderAsync_FormatCurrency_WithDollarSign_PrefixesDollar()
    {
        var template = "Price: {{ format_currency price \"$\" }}";

        var data = new Dictionary<string, object?>
        {
            ["price"] = 9.99m
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("Price: $9.99");
    }

    [Fact]
    public async Task RenderAsync_FormatCurrency_NoSymbol_NoPrefix()
    {
        var template = "{{ format_currency amount \"\" }}";

        var data = new Dictionary<string, object?>
        {
            ["amount"] = 500.00m
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("500.00");
    }

    [Fact]
    public async Task RenderAsync_FormatCurrency_NullValue_ReturnsEmptyString()
    {
        var template = "{{ format_currency price \"€\" }}";

        var data = new Dictionary<string, object?>
        {
            ["price"] = null
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_FormatCurrency_InTableRow_FormatsEachAmount()
    {
        var template = """
            {{ for row in lines }}
            <td>{{ format_currency row.price "€" }}</td>
            {{ end }}
            """;

        var data = new Dictionary<string, object?>
        {
            ["lines"] = new List<object?>
            {
                new Dictionary<string, object?> { ["price"] = 9.99m },
                new Dictionary<string, object?> { ["price"] = 1999.00m }
            }
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<td>€9.99</td>");
        result.Should().Contain("<td>€1,999.00</td>");
    }

    // ----------------------------------------------------------------
    // Empty collection handling (AC-5)
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_EmptyCollectionInComplexTemplate_HandleGracefully()
    {
        // Acceptance criterion: Empty collections handled gracefully.
        var template = """
            <p>Dear {{ customer_name }},</p>
            {{ if has_orders }}
            <table>
            {{ for order in orders }}
            <tr><td>{{ order.ref }}</td></tr>
            {{ end }}
            </table>
            {{ else }}
            <p>No orders found.</p>
            {{ end }}
            """;

        var dataEmpty = new Dictionary<string, object?>
        {
            ["customer_name"] = "Alice",
            ["has_orders"] = false,
            ["orders"] = new List<object?>()
        };

        var result = await _renderer.RenderAsync(template, dataEmpty);

        result.Should().Contain("<p>Dear Alice,</p>");
        result.Should().Contain("<p>No orders found.</p>");
        result.Should().NotContain("<table>");
        result.Should().NotContain("<tr>");
    }

    // ----------------------------------------------------------------
    // Full email template scenario
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_FullEmailTemplate_RendersCorrectly()
    {
        var template = """
            <p>Dear {{ first_name }} {{ last_name }},</p>
            <p>Invoice date: {{ format_date invoice_date "dd/MM/yyyy" }}</p>
            {{ if has_discount }}
            <p>Discount applied: {{ discount_percent }}%</p>
            {{ end }}
            <table>
            {{ for line in order_lines }}
            <tr>
              <td>{{ line.product }}</td>
              <td>{{ line.qty }}</td>
              <td>{{ format_currency line.price "€" }}</td>
            </tr>
            {{ end }}
            </table>
            <p>Total: {{ format_currency total "€" }}</p>
            """;

        var data = new Dictionary<string, object?>
        {
            ["first_name"] = "Alice",
            ["last_name"] = "Dupont",
            ["invoice_date"] = new DateTime(2026, 3, 19),
            ["has_discount"] = true,
            ["discount_percent"] = 10,
            ["order_lines"] = new List<object?>
            {
                new Dictionary<string, object?> { ["product"] = "Widget A", ["qty"] = 2, ["price"] = 9.99m },
                new Dictionary<string, object?> { ["product"] = "Widget B", ["qty"] = 1, ["price"] = 24.99m }
            },
            ["total"] = 44.97m
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<p>Dear Alice Dupont,</p>");
        result.Should().Contain("Invoice date: 19/03/2026");
        result.Should().Contain("Discount applied: 10%");
        result.Should().Contain("<td>Widget A</td>");
        result.Should().Contain("<td>€9.99</td>");
        result.Should().Contain("<td>Widget B</td>");
        result.Should().Contain("<td>€24.99</td>");
        result.Should().Contain("Total: €44.97");
    }
}
