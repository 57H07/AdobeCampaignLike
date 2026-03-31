using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CampaignEngine.Infrastructure.Rendering;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// Unit tests for <see cref="DocxPlaceholderReplacer"/>.
///
/// Covers:
/// - TASK-014-06: Scalar placeholder replacement in main body, headers, footers.
/// - TASK-014-07: XML-escaping of values containing &lt;, &gt;, &amp;, &quot;,
///   &apos;.
/// - TASK-014-08: Missing keys are replaced with empty string (no exception).
///
/// All DOCX documents are built purely in-memory using
/// <see cref="DocxPlaceholderReplacerFixtures"/>; no binary files are committed.
/// </summary>
public class DocxPlaceholderReplacerTests
{
    private readonly DocxPlaceholderReplacer _sut = new();

    // ----------------------------------------------------------------
    // TASK-014-06: Basic scalar replacement — main body
    // ----------------------------------------------------------------

    [Fact]
    public void ReplaceScalars_SimpleKey_ReplacedWithValue()
    {
        // Arrange
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("Hello {{ firstName }}!");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["firstName"] = "Alice" };

        // Act
        _sut.ReplaceScalars(doc, data);

        // Assert
        GetBodyText(doc).Should().Be("Hello Alice!");
    }

    [Fact]
    public void ReplaceScalars_MultipleDistinctKeys_AllReplaced()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ firstName }} {{ lastName }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string>
        {
            ["firstName"] = "Alice",
            ["lastName"] = "Smith"
        };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("Alice Smith");
    }

    [Fact]
    public void ReplaceScalars_SameKeyTwice_BothOccurrencesReplaced()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ name }}, dear {{ name }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["name"] = "Bob" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("Bob, dear Bob");
    }

    [Fact]
    public void ReplaceScalars_SpacesInsideBraces_AreNormalisedAndReplaced()
    {
        // Business Rule 1: spaces inside braces are optional — both forms match
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{firstName}} and {{ firstName }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["firstName"] = "Carol" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("Carol and Carol");
    }

    [Fact]
    public void ReplaceScalars_KeysAreCaseSensitive_WrongCaseIsNotReplaced()
    {
        // Business Rule 2: keys are case-sensitive
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ FirstName }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["firstName"] = "Alice" };

        _sut.ReplaceScalars(doc, data);

        // "FirstName" != "firstName" — falls through to empty string
        GetBodyText(doc).Should().Be(string.Empty);
    }

    [Fact]
    public void ReplaceScalars_NoPlaceholders_TextLeftUntouched()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("Plain text with no tokens.");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["name"] = "Alice" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("Plain text with no tokens.");
    }

    // ----------------------------------------------------------------
    // TASK-014-06: Replacement in headers and footers
    // ----------------------------------------------------------------

    [Fact]
    public void ReplaceScalars_PlaceholderInHeader_IsReplaced()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithHeader("Dear {{ firstName }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["firstName"] = "Diana" };

        _sut.ReplaceScalars(doc, data);

        var headerText = GetHeaderText(doc);
        headerText.Should().Be("Dear Diana");
    }

    [Fact]
    public void ReplaceScalars_PlaceholderInFooter_IsReplaced()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithFooter("Page — {{ companyName }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["companyName"] = "Acme Corp" };

        _sut.ReplaceScalars(doc, data);

        var footerText = GetFooterText(doc);
        footerText.Should().Be("Page — Acme Corp");
    }

    [Fact]
    public void ReplaceScalars_PlaceholdersInBodyAndHeaderAndFooter_AllReplaced()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyHeaderFooter(
            bodyText: "Hello {{ firstName }}",
            headerText: "Header: {{ city }}",
            footerText: "Footer: {{ country }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string>
        {
            ["firstName"] = "Eve",
            ["city"] = "Paris",
            ["country"] = "France"
        };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("Hello Eve");
        GetHeaderText(doc).Should().Be("Header: Paris");
        GetFooterText(doc).Should().Be("Footer: France");
    }

    // ----------------------------------------------------------------
    // TASK-014-07: XML escaping
    // ----------------------------------------------------------------

    [Fact]
    public void ReplaceScalars_ValueContainsAmpersand_IsXmlEscaped()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ company }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["company"] = "Alice & Bob Ltd" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("Alice &amp; Bob Ltd");
    }

    [Fact]
    public void ReplaceScalars_ValueContainsLessThan_IsXmlEscaped()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ expr }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["expr"] = "x < 10" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("x &lt; 10");
    }

    [Fact]
    public void ReplaceScalars_ValueContainsGreaterThan_IsXmlEscaped()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ expr }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["expr"] = "x > 5" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("x &gt; 5");
    }

    [Fact]
    public void ReplaceScalars_ValueContainsDoubleQuote_IsXmlEscaped()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ quote }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["quote"] = "He said \"hello\"" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("He said &quot;hello&quot;");
    }

    [Fact]
    public void ReplaceScalars_ValueContainsAllSpecialChars_AllEscaped()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ val }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["val"] = "<>&\"'" };

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("&lt;&gt;&amp;&quot;&apos;");
    }

    [Theory]
    [InlineData("<script>", "&lt;script&gt;")]
    [InlineData("Tom & Jerry", "Tom &amp; Jerry")]
    [InlineData("O'Brien", "O&apos;Brien")]
    public void XmlEscape_SpecialCharacters_AreEscaped(string input, string expected)
    {
        // Direct unit test of the escaping helper (internal access)
        DocxPlaceholderReplacer.XmlEscape(input).Should().Be(expected);
    }

    [Fact]
    public void XmlEscape_PlainText_ReturnedUnchanged()
    {
        DocxPlaceholderReplacer.XmlEscape("Hello World").Should().Be("Hello World");
    }

    [Fact]
    public void XmlEscape_EmptyString_ReturnedUnchanged()
    {
        DocxPlaceholderReplacer.XmlEscape(string.Empty).Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // TASK-014-08: Missing keys replaced with empty string
    // ----------------------------------------------------------------

    [Fact]
    public void ReplaceScalars_MissingKey_ReplacedWithEmptyString()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("Hello {{ unknownKey }}!");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string>(); // no matching key

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("Hello !");
    }

    [Fact]
    public void ReplaceScalars_EmptyData_AllPlaceholdersBecomEmpty()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ a }} {{ b }} {{ c }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string>();

        _sut.ReplaceScalars(doc, data);

        GetBodyText(doc).Should().Be("  ");
    }

    [Fact]
    public void ReplaceScalars_MixedPresentAndMissingKeys_MissingBecomesEmpty()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ first }} {{ last }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string> { ["first"] = "Alice" };

        _sut.ReplaceScalars(doc, data);

        // "first" is present, "last" is missing → empty string
        GetBodyText(doc).Should().Be("Alice ");
    }

    [Fact]
    public void ReplaceScalars_MissingKey_DoesNotThrow()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ missing }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var data = new Dictionary<string, string>();

        var act = () => _sut.ReplaceScalars(doc, data);
        act.Should().NotThrow();
    }

    // ----------------------------------------------------------------
    // Guard clauses
    // ----------------------------------------------------------------

    [Fact]
    public void ReplaceScalars_NullDocument_ThrowsArgumentNullException()
    {
        var act = () => _sut.ReplaceScalars(null!, new Dictionary<string, string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReplaceScalars_NullData_ThrowsArgumentNullException()
    {
        using var stream = DocxPlaceholderReplacerFixtures.CreateDocxWithBodyText("{{ x }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var act = () => _sut.ReplaceScalars(doc, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private static string GetBodyText(WordprocessingDocument doc)
        => string.Concat(
            doc.MainDocumentPart!.Document!.Body!
               .Descendants<Text>()
               .Select(t => t.Text));

    private static string GetHeaderText(WordprocessingDocument doc)
        => string.Concat(
            doc.MainDocumentPart!.HeaderParts.First().Header
               .Descendants<Text>()
               .Select(t => t.Text));

    private static string GetFooterText(WordprocessingDocument doc)
        => string.Concat(
            doc.MainDocumentPart!.FooterParts.First().Footer
               .Descendants<Text>()
               .Select(t => t.Text));
}

/// <summary>
/// In-memory DOCX factory for <see cref="DocxPlaceholderReplacerTests"/>.
///
/// All documents contain pre-merged runs so that the tests focus solely on
/// scalar replacement (run merging is the responsibility of
/// <see cref="DocxRunMerger"/>).
/// </summary>
internal static class DocxPlaceholderReplacerFixtures
{
    /// <summary>Creates a minimal DOCX with a single body paragraph run.</summary>
    public static Stream CreateDocxWithBodyText(string text)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(BuildParagraph(text)));
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates a DOCX with a header containing the given text.</summary>
    public static Stream CreateDocxWithHeader(string headerText)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph()));

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(BuildParagraph(headerText));
            headerPart.Header.Save();

            var sectPr = new SectionProperties(
                new HeaderReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = mainPart.GetIdOfPart(headerPart)
                });
            mainPart.Document.Body!.AppendChild(sectPr);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates a DOCX with a footer containing the given text.</summary>
    public static Stream CreateDocxWithFooter(string footerText)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph()));

            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(BuildParagraph(footerText));
            footerPart.Footer.Save();

            var sectPr = new SectionProperties(
                new FooterReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = mainPart.GetIdOfPart(footerPart)
                });
            mainPart.Document.Body!.AppendChild(sectPr);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with a body paragraph, a header, and a footer.
    /// </summary>
    public static Stream CreateDocxWithBodyHeaderFooter(
        string bodyText,
        string headerText,
        string footerText)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(BuildParagraph(bodyText)));

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(BuildParagraph(headerText));
            headerPart.Header.Save();

            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(BuildParagraph(footerText));
            footerPart.Footer.Save();

            var sectPr = new SectionProperties(
                new HeaderReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = mainPart.GetIdOfPart(headerPart)
                },
                new FooterReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = mainPart.GetIdOfPart(footerPart)
                });
            mainPart.Document.Body!.AppendChild(sectPr);
        }
        ms.Position = 0;
        return ms;
    }

    // ---------------------------------------------------------------
    // Private builders
    // ---------------------------------------------------------------

    private static Paragraph BuildParagraph(string text)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return new Paragraph(run);
    }
}
