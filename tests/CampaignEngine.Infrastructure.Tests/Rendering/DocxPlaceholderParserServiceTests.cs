using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Infrastructure.Rendering;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// Unit tests for <see cref="DocxPlaceholderParserService"/>.
///
/// Covers US-018 TASK-018-04:
/// - Comparing extracted placeholders against a manifest.
/// - Returning warnings for undeclared keys.
/// - Non-blocking: no exceptions thrown for undeclared keys.
/// - Edge cases: empty manifest, fully-declared manifest, no placeholders in DOCX.
/// </summary>
public class DocxPlaceholderParserServiceTests
{
    private readonly DocxPlaceholderParserService _sut = new(new DocxPlaceholderParser());

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static PlaceholderManifestEntryDto Manifest(string key) =>
        new() { Id = Guid.NewGuid(), TemplateId = Guid.NewGuid(), Key = key, Type = "Scalar", IsFromDataSource = true };

    // ================================================================
    // All placeholders declared → empty warnings
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_AllKeysDeclared_ReturnsEmpty()
    {
        // Arrange
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText(
            "Hello {{ firstName }} {{ lastName }}!");
        var manifest = new[] { Manifest("firstName"), Manifest("lastName") };

        // Act
        var warnings = _sut.GetUndeclaredPlaceholders(stream, manifest);

        // Assert
        warnings.Should().BeEmpty();
    }

    // ================================================================
    // Some undeclared → only undeclared keys returned
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_SomeUndeclared_ReturnsMissingKeys()
    {
        // Arrange — "invoiceDate" is in DOCX but absent from manifest
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText(
            "Customer: {{ customerName }}, Date: {{ invoiceDate }}");
        var manifest = new[] { Manifest("customerName") };

        // Act
        var warnings = _sut.GetUndeclaredPlaceholders(stream, manifest);

        // Assert
        warnings.Should().ContainSingle().Which.Should().Be("invoiceDate");
    }

    // ================================================================
    // Empty manifest → all extracted keys are warned
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_EmptyManifest_ReturnsAllExtractedKeys()
    {
        // Arrange — typical state for a brand-new template upload (no manifest yet)
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText(
            "{{ firstName }} {{ lastName }} {{ amount }}");
        var manifest = Array.Empty<PlaceholderManifestEntryDto>();

        // Act
        var warnings = _sut.GetUndeclaredPlaceholders(stream, manifest);

        // Assert
        warnings.Should().BeEquivalentTo(new[] { "firstName", "lastName", "amount" });
    }

    // ================================================================
    // No placeholders in DOCX → always empty warnings
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_NoPlaceholdersInDocx_ReturnsEmpty()
    {
        // Arrange
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("Plain text, no placeholders.");
        var manifest = new[] { Manifest("someKey") };

        // Act
        var warnings = _sut.GetUndeclaredPlaceholders(stream, manifest);

        // Assert — no placeholders extracted, nothing to warn about
        warnings.Should().BeEmpty();
    }

    // ================================================================
    // Key comparison is ordinal (case-sensitive)
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_CaseMismatch_TreatedAsUndeclared()
    {
        // Arrange — manifest has "CustomerName" but DOCX uses "customerName".
        // Surround with text so the paragraph is not mistaken for a collection marker.
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("Dear {{ customerName }},");
        var manifest = new[] { Manifest("CustomerName") }; // different casing

        // Act
        var warnings = _sut.GetUndeclaredPlaceholders(stream, manifest);

        // Assert — ordinal comparison: "customerName" ≠ "CustomerName"
        warnings.Should().ContainSingle().Which.Should().Be("customerName");
    }

    // ================================================================
    // Structural markers excluded — they must not appear in warnings
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_IfAndEndMarkersInDocx_NotWarned()
    {
        // Arrange — {{ if condition }} and {{ end }} are structural, not data keys
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyParagraphs(
            "{{ if hasPremium }}",
            "Premium content: {{ offerCode }}",
            "{{ end }}");
        var manifest = new[] { Manifest("offerCode") };

        // Act
        var warnings = _sut.GetUndeclaredPlaceholders(stream, manifest);

        // Assert — only data placeholders can trigger warnings; structural markers are excluded
        warnings.Should().BeEmpty();
    }

    // ================================================================
    // Duplicate keys in DOCX → each unique key warned at most once
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_DuplicateKeyInDocx_WarnedOnlyOnce()
    {
        // Arrange — "name" used twice; manifest is empty
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText(
            "Dear {{ name }}, thank you {{ name }}.");
        var manifest = Array.Empty<PlaceholderManifestEntryDto>();

        // Act
        var warnings = _sut.GetUndeclaredPlaceholders(stream, manifest);

        // Assert
        warnings.Should().ContainSingle().Which.Should().Be("name");
    }

    // ================================================================
    // Null guards
    // ================================================================

    [Fact]
    public void GetUndeclaredPlaceholders_NullStream_Throws()
    {
        var act = () => _sut.GetUndeclaredPlaceholders(null!, Array.Empty<PlaceholderManifestEntryDto>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetUndeclaredPlaceholders_NullManifest_Throws()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("{{ key }}");
        var act = () => _sut.GetUndeclaredPlaceholders(stream, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
