using CampaignEngine.Application.Models;
using CampaignEngine.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// TASK-012-07: Unit tests for conditional block evaluation.
/// Verifies that the ScribanTemplateRenderer correctly evaluates boolean expressions
/// and renders conditional content blocks.
/// </summary>
public class AdvancedRenderingConditionalTests
{
    private readonly ScribanTemplateRenderer _renderer;

    public AdvancedRenderingConditionalTests()
    {
        _renderer = new ScribanTemplateRenderer(NullLogger<ScribanTemplateRenderer>.Instance);
    }

    // ----------------------------------------------------------------
    // Basic if/end
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ConditionTrue_RendersConditionalBlock()
    {
        // Business rule: Conditional syntax: {{ if condition }} content {{ end }}
        var template = "{{ if is_premium }}<p>Premium member</p>{{ end }}";
        var data = new Dictionary<string, object?> { ["is_premium"] = true };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Contain("<p>Premium member</p>");
    }

    [Fact]
    public async Task RenderAsync_ConditionFalse_DoesNotRenderConditionalBlock()
    {
        var template = "{{ if is_premium }}<p>Premium member</p>{{ end }}";
        var data = new Dictionary<string, object?> { ["is_premium"] = false };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().NotContain("<p>Premium member</p>");
        result.Trim().Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // if / else
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ConditionTrueWithElse_RendersIfBranch()
    {
        var template = "{{ if is_vip }}VIP{{ else }}Standard{{ end }}";
        var data = new Dictionary<string, object?> { ["is_vip"] = true };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("VIP");
    }

    [Fact]
    public async Task RenderAsync_ConditionFalseWithElse_RendersElseBranch()
    {
        var template = "{{ if is_vip }}VIP{{ else }}Standard{{ end }}";
        var data = new Dictionary<string, object?> { ["is_vip"] = false };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("Standard");
    }

    // ----------------------------------------------------------------
    // if / else if / else
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_ElseIfChain_RendersCorrectBranch()
    {
        var template = "{{ if score >= 90 }}A{{ else if score >= 70 }}B{{ else }}C{{ end }}";

        var data90 = new Dictionary<string, object?> { ["score"] = 95 };
        var data70 = new Dictionary<string, object?> { ["score"] = 75 };
        var dataLow = new Dictionary<string, object?> { ["score"] = 55 };

        var resultA = await _renderer.RenderAsync(template, data90);
        var resultB = await _renderer.RenderAsync(template, data70);
        var resultC = await _renderer.RenderAsync(template, dataLow);

        resultA.Should().Be("A");
        resultB.Should().Be("B");
        resultC.Should().Be("C");
    }

    // ----------------------------------------------------------------
    // Null / missing variable checks
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_CheckForNull_RendersBlockWhenNotNull()
    {
        var template = "{{ if promo_code != null && promo_code != \"\" }}<p>Code: {{ promo_code }}</p>{{ end }}";

        var dataWithCode = new Dictionary<string, object?> { ["promo_code"] = "SUMMER25" };
        var dataNoCode = new Dictionary<string, object?> { ["promo_code"] = null };
        var dataEmpty = new Dictionary<string, object?> { ["promo_code"] = "" };

        var withCode = await _renderer.RenderAsync(template, dataWithCode);
        var noCode = await _renderer.RenderAsync(template, dataNoCode);
        var empty = await _renderer.RenderAsync(template, dataEmpty);

        withCode.Should().Contain("<p>Code: SUMMER25</p>");
        noCode.Trim().Should().BeEmpty();
        empty.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_MissingConditionVariable_TreatedAsFalsy()
    {
        // StrictVariables = false: missing variables are null/falsy — no exception
        var template = "{{ if show_banner }}<div>Banner</div>{{ end }}";
        var data = new Dictionary<string, object?>();

        var result = await _renderer.RenderAsync(template, data);

        result.Trim().Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Boolean expressions (AND, OR, NOT, comparison)
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_AndExpression_BothTrueRendersBlock()
    {
        var template = "{{ if is_active && has_subscription }}Active subscriber{{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["is_active"] = true,
            ["has_subscription"] = true
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("Active subscriber");
    }

    [Fact]
    public async Task RenderAsync_AndExpression_OneFalseDoesNotRenderBlock()
    {
        var template = "{{ if is_active && has_subscription }}Active subscriber{{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["is_active"] = true,
            ["has_subscription"] = false
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_OrExpression_AnyTrueRendersBlock()
    {
        var template = "{{ if is_premium || is_trial }}Privileged user{{ end }}";

        var data = new Dictionary<string, object?>
        {
            ["is_premium"] = false,
            ["is_trial"] = true
        };

        var result = await _renderer.RenderAsync(template, data);

        result.Should().Be("Privileged user");
    }

    [Fact]
    public async Task RenderAsync_NotExpression_InvertsBoolean()
    {
        var template = "{{ if !is_blocked }}Welcome!{{ end }}";

        var dataUnblocked = new Dictionary<string, object?> { ["is_blocked"] = false };
        var dataBlocked = new Dictionary<string, object?> { ["is_blocked"] = true };

        var welcome = await _renderer.RenderAsync(template, dataUnblocked);
        var blocked = await _renderer.RenderAsync(template, dataBlocked);

        welcome.Should().Be("Welcome!");
        blocked.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_ComparisonExpression_EqualityCheck()
    {
        var template = "{{ if status == \"active\" }}Active{{ else }}Inactive{{ end }}";

        var dataActive = new Dictionary<string, object?> { ["status"] = "active" };
        var dataInactive = new Dictionary<string, object?> { ["status"] = "pending" };

        var active = await _renderer.RenderAsync(template, dataActive);
        var inactive = await _renderer.RenderAsync(template, dataInactive);

        // Note: HTML encoding applies to data, not template literals
        // The data value "active" has no HTML special chars so comparison works normally
        active.Should().Be("Active");
        inactive.Should().Be("Inactive");
    }

    [Fact]
    public async Task RenderAsync_NumericComparison_GreaterThan()
    {
        var template = "{{ if balance > 0 }}Positive balance{{ else }}No balance{{ end }}";

        var positive = new Dictionary<string, object?> { ["balance"] = 100.50m };
        var zero = new Dictionary<string, object?> { ["balance"] = 0 };

        var positiveResult = await _renderer.RenderAsync(template, positive);
        var zeroResult = await _renderer.RenderAsync(template, zero);

        positiveResult.Should().Be("Positive balance");
        zeroResult.Should().Be("No balance");
    }
}
