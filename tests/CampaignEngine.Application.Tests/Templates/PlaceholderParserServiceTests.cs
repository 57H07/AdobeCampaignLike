using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Infrastructure.Templates;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Unit tests for PlaceholderParserService.
/// Validates placeholder extraction from Scriban template HTML and manifest completeness validation.
/// TASK-006-07: Unit tests for placeholder extraction.
/// TASK-006-08: Validation tests for manifest completeness.
/// </summary>
public class PlaceholderParserServiceTests
{
    private readonly PlaceholderParserService _parser = new();

    // ================================================================
    // ExtractPlaceholders — Scalar
    // ================================================================

    [Fact]
    public void ExtractPlaceholders_SimpleScalar_ExtractsKey()
    {
        // Arrange
        const string html = "<p>Hello {{ customerName }}!</p>";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.ScalarKeys.Should().ContainSingle().Which.Should().Be("customerName");
        result.IterationKeys.Should().BeEmpty();
        result.AllKeys.Should().ContainSingle().Which.Should().Be("customerName");
    }

    [Fact]
    public void ExtractPlaceholders_NoSpaces_ExtractsKey()
    {
        // Arrange — {{key}} without spaces
        const string html = "<p>{{invoiceNumber}}</p>";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.ScalarKeys.Should().ContainSingle().Which.Should().Be("invoiceNumber");
    }

    [Fact]
    public void ExtractPlaceholders_MultipleScalars_ExtractsAll()
    {
        // Arrange
        const string html = "<p>{{ firstName }} {{ lastName }} — {{ email }}</p>";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.ScalarKeys.Should().HaveCount(3);
        result.ScalarKeys.Should().Contain("firstName");
        result.ScalarKeys.Should().Contain("lastName");
        result.ScalarKeys.Should().Contain("email");
    }

    [Fact]
    public void ExtractPlaceholders_DuplicateScalarKey_DeduplicatesResult()
    {
        // Arrange — same key appears twice
        const string html = "<p>{{ name }}</p><p>{{ name }}</p>";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.ScalarKeys.Should().ContainSingle().Which.Should().Be("name");
    }

    [Fact]
    public void ExtractPlaceholders_EmptyBody_ReturnsEmpty()
    {
        // Act
        var result = _parser.ExtractPlaceholders(string.Empty);

        // Assert
        result.ScalarKeys.Should().BeEmpty();
        result.IterationKeys.Should().BeEmpty();
        result.AllKeys.Should().BeEmpty();
    }

    [Fact]
    public void ExtractPlaceholders_NullBody_ReturnsEmpty()
    {
        // Act
        var result = _parser.ExtractPlaceholders(null!);

        // Assert
        result.AllKeys.Should().BeEmpty();
    }

    // ================================================================
    // ExtractPlaceholders — Iteration (Table/List)
    // ================================================================

    [Fact]
    public void ExtractPlaceholders_ForLoop_ExtractsCollectionKey()
    {
        // Arrange — Scriban for-in pattern
        const string html = "{{ for row in orders }}<tr><td>{{ row.product }}</td></tr>{{ end }}";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.IterationKeys.Should().ContainSingle().Which.Should().Be("orders");
    }

    [Fact]
    public void ExtractPlaceholders_ForLoop_AliasNotInScalar()
    {
        // Arrange — "row" is the loop alias, should NOT appear as scalar key
        const string html = "{{ for row in orders }}<td>{{ row.amount }}</td>{{ end }}";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.ScalarKeys.Should().NotContain("row");
        result.IterationKeys.Should().ContainSingle("orders");
    }

    [Fact]
    public void ExtractPlaceholders_MultipleForLoops_ExtractsAllCollections()
    {
        // Arrange
        const string html = @"
            {{ for item in products }}<p>{{ item.name }}</p>{{ end }}
            {{ for line in addressLines }}<p>{{ line }}</p>{{ end }}";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.IterationKeys.Should().HaveCount(2);
        result.IterationKeys.Should().Contain("products");
        result.IterationKeys.Should().Contain("addressLines");
    }

    [Fact]
    public void ExtractPlaceholders_MixedScalarAndTable_ClassifiesCorrectly()
    {
        // Arrange
        const string html = @"
            <p>Dear {{ firstName }},</p>
            {{ for row in invoiceLines }}
              <tr><td>{{ row.description }}</td><td>{{ row.amount }}</td></tr>
            {{ end }}
            <p>Total: {{ totalAmount }}</p>";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.ScalarKeys.Should().Contain("firstName");
        result.ScalarKeys.Should().Contain("totalAmount");
        result.IterationKeys.Should().ContainSingle().Which.Should().Be("invoiceLines");
        result.AllKeys.Should().HaveCount(3); // firstName, totalAmount, invoiceLines
    }

    // ================================================================
    // ExtractPlaceholders — Keyword filtering
    // ================================================================

    [Fact]
    public void ExtractPlaceholders_ScribanKeywords_AreNotExtracted()
    {
        // Arrange — template with if/else/end keywords (not placeholders)
        const string html = "{{ if condition }}Yes{{ else }}No{{ end }}";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.AllKeys.Should().NotContain("if");
        result.AllKeys.Should().NotContain("else");
        result.AllKeys.Should().NotContain("end");
    }

    [Fact]
    public void ExtractPlaceholders_BooleanLiterals_AreNotExtracted()
    {
        // Arrange
        const string html = "{{ if flag == true }}shown{{ end }}";

        // Act
        var result = _parser.ExtractPlaceholders(html);

        // Assert
        result.AllKeys.Should().NotContain("true");
        result.AllKeys.Should().NotContain("false");
    }

    // ================================================================
    // ValidateManifestCompleteness
    // ================================================================

    [Fact]
    public void ValidateManifestCompleteness_AllDeclared_IsCompleteTrue()
    {
        // Arrange
        const string html = "<p>{{ name }} — {{ email }}</p>";
        var manifest = new[]
        {
            MakeEntry("name", "Scalar"),
            MakeEntry("email", "Scalar")
        };

        // Act
        var result = _parser.ValidateManifestCompleteness(html, manifest);

        // Assert
        result.IsComplete.Should().BeTrue();
        result.UndeclaredKeys.Should().BeEmpty();
        result.OrphanKeys.Should().BeEmpty();
    }

    [Fact]
    public void ValidateManifestCompleteness_MissingDeclaration_IsCompleteFalse()
    {
        // Arrange — template uses "city" but manifest only declares "name"
        const string html = "<p>{{ name }} from {{ city }}</p>";
        var manifest = new[]
        {
            MakeEntry("name", "Scalar")
        };

        // Act
        var result = _parser.ValidateManifestCompleteness(html, manifest);

        // Assert
        result.IsComplete.Should().BeFalse();
        result.UndeclaredKeys.Should().ContainSingle().Which.Should().Be("city");
        result.OrphanKeys.Should().BeEmpty();
    }

    [Fact]
    public void ValidateManifestCompleteness_OrphanEntry_ReportedButStillComplete()
    {
        // Arrange — manifest declares "unused" which isn't in template
        const string html = "<p>{{ name }}</p>";
        var manifest = new[]
        {
            MakeEntry("name", "Scalar"),
            MakeEntry("unused", "Scalar")
        };

        // Act
        var result = _parser.ValidateManifestCompleteness(html, manifest);

        // Assert
        result.IsComplete.Should().BeTrue(); // no undeclared keys → complete
        result.OrphanKeys.Should().ContainSingle().Which.Should().Be("unused");
    }

    [Fact]
    public void ValidateManifestCompleteness_EmptyManifestEmptyTemplate_IsCompleteTrue()
    {
        // Arrange
        const string html = "<p>No placeholders here.</p>";
        var manifest = Array.Empty<PlaceholderManifestEntryDto>();

        // Act
        var result = _parser.ValidateManifestCompleteness(html, manifest);

        // Assert
        result.IsComplete.Should().BeTrue();
        result.UndeclaredKeys.Should().BeEmpty();
        result.OrphanKeys.Should().BeEmpty();
    }

    [Fact]
    public void ValidateManifestCompleteness_KeyMatchIsCaseInsensitive()
    {
        // Arrange — manifest declares "Name" but template uses "name" (different case)
        const string html = "<p>{{ name }}</p>";
        var manifest = new[]
        {
            MakeEntry("Name", "Scalar") // capital N
        };

        // Act
        var result = _parser.ValidateManifestCompleteness(html, manifest);

        // Assert
        result.IsComplete.Should().BeTrue(); // case-insensitive match
    }

    [Fact]
    public void ValidateManifestCompleteness_IterationKeyDeclaredAsTable_IsComplete()
    {
        // Arrange
        const string html = "{{ for row in orders }}<td>{{ row.item }}</td>{{ end }}";
        var manifest = new[]
        {
            MakeEntry("orders", "Table")
        };

        // Act
        var result = _parser.ValidateManifestCompleteness(html, manifest);

        // Assert
        result.IsComplete.Should().BeTrue();
        result.UndeclaredKeys.Should().BeEmpty();
    }

    [Fact]
    public void ValidateManifestCompleteness_SummaryContainsUndeclaredKeys()
    {
        // Arrange
        const string html = "{{ missing1 }} {{ missing2 }}";
        var manifest = Array.Empty<PlaceholderManifestEntryDto>();

        // Act
        var result = _parser.ValidateManifestCompleteness(html, manifest);

        // Assert
        result.Summary.Should().Contain("missing1");
        result.Summary.Should().Contain("missing2");
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static PlaceholderManifestEntryDto MakeEntry(string key, string type) => new()
    {
        Id = Guid.NewGuid(),
        TemplateId = Guid.NewGuid(),
        Key = key,
        Type = type,
        IsFromDataSource = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
