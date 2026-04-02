using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CampaignEngine.Infrastructure.Rendering;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// Unit tests for <see cref="DocxPlaceholderParser"/>.
///
/// Covers:
/// - TASK-017-06: Placeholder extraction from the main document body.
/// - TASK-017-07: Placeholder extraction from headers and footers.
///
/// All DOCX documents are built purely in-memory; no binary files are committed.
/// </summary>
public class DocxPlaceholderParserTests
{
    private readonly DocxPlaceholderParser _sut = new();

    // ----------------------------------------------------------------
    // TASK-017-06: Main body extraction
    // ----------------------------------------------------------------

    [Fact]
    public void ExtractPlaceholders_SingleScalarKey_Returned()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("Hello {{ firstName }}!");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().ContainSingle().Which.Should().Be("firstName");
    }

    [Fact]
    public void ExtractPlaceholders_MultipleDistinctKeys_AllReturned()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("{{ firstName }} {{ lastName }}, age {{ age }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().Equal("firstName", "lastName", "age");
    }

    [Fact]
    public void ExtractPlaceholders_DuplicateKey_ReturnedOnlyOnce()
    {
        // TASK-017-05: deduplication
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("{{ name }}, dear {{ name }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().ContainSingle().Which.Should().Be("name");
    }

    [Fact]
    public void ExtractPlaceholders_NoPlaceholders_ReturnsEmpty()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("Plain text.");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractPlaceholders_EndMarker_Excluded()
    {
        // {{ end }} is a structural marker, not a data placeholder
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyParagraphs("{{ end }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractPlaceholders_IfMarker_Excluded()
    {
        // {{ if conditionKey }} is a structural marker, not a data placeholder
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyParagraphs("{{ if showSection }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractPlaceholders_CollectionMarkerParagraph_Excluded()
    {
        // A paragraph containing only {{ collectionKey }} is a collection marker
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyParagraphs("{{ orders }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractPlaceholders_ItemDotField_Included()
    {
        // {{ item.field }} placeholders are data placeholders — must be kept
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("{{ item.productName }} qty {{ item.quantity }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().Equal("item.productName", "item.quantity");
    }

    [Fact]
    public void ExtractPlaceholders_MixedScalarsAndStructuralMarkers_OnlyScalarsReturned()
    {
        // Document with structural markers interspersed with scalar placeholders
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyParagraphs(
            "Dear {{ firstName }} {{ lastName }},",
            "{{ if showOffer }}",
            "Your offer: {{ offerCode }}",
            "{{ end }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().Equal("firstName", "lastName", "offerCode");
    }

    [Fact]
    public void ExtractPlaceholders_KeysAreCaseSensitive()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("{{ firstName }} {{ FirstName }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().Equal("firstName", "FirstName");
    }

    [Fact]
    public void ExtractPlaceholders_SpacesInsideBraces_KeyTrimmed()
    {
        // Spaces inside braces are trimmed — use inline context so the paragraph
        // is not mistaken for a standalone collection marker.
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyText("Dear {{  firstName  }},");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().ContainSingle().Which.Should().Be("firstName");
    }

    [Fact]
    public void ExtractPlaceholders_NullDoc_ThrowsArgumentNullException()
    {
        var act = () => _sut.ExtractPlaceholders(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----------------------------------------------------------------
    // TASK-017-07: Header and footer extraction
    // ----------------------------------------------------------------

    [Fact]
    public void ExtractPlaceholders_KeyInHeader_Returned()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithHeader("Page {{ pageNumber }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().Contain("pageNumber");
    }

    [Fact]
    public void ExtractPlaceholders_KeyInFooter_Returned()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithFooter("Ref: {{ refCode }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().Contain("refCode");
    }

    [Fact]
    public void ExtractPlaceholders_KeysInBodyHeaderFooter_AllReturned()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyHeaderFooter(
            bodyText: "Dear {{ firstName }},",
            headerText: "Doc {{ docTitle }}",
            footerText: "Page {{ pageNum }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().Contain("firstName")
              .And.Contain("docTitle")
              .And.Contain("pageNum");
    }

    [Fact]
    public void ExtractPlaceholders_SameKeyInBodyAndHeader_ReturnedOnlyOnce()
    {
        // Deduplication must work across sections
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithBodyHeaderFooter(
            bodyText: "Hello {{ name }}",
            headerText: "Hi {{ name }}",
            footerText: string.Empty);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().ContainSingle(k => k == "name");
    }

    [Fact]
    public void ExtractPlaceholders_EndMarkerInHeader_Excluded()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithHeader("{{ end }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractPlaceholders_CollectionMarkerInHeader_Excluded()
    {
        using var stream = DocxPlaceholderParserFixtures.CreateDocxWithHeader("{{ items }}");
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var result = _sut.ExtractPlaceholders(doc);

        result.Should().BeEmpty();
    }
}

// ---------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------

/// <summary>
/// In-memory DOCX factory helpers for <see cref="DocxPlaceholderParserTests"/>.
/// </summary>
internal static class DocxPlaceholderParserFixtures
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

    /// <summary>
    /// Creates a DOCX whose body contains one paragraph per string in
    /// <paramref name="paragraphs"/>.
    /// </summary>
    public static Stream CreateDocxWithBodyParagraphs(params string[] paragraphs)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
                body.AppendChild(BuildParagraph(text));
            mainPart.Document = new Document(body);
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

    /// <summary>Creates a DOCX with a body paragraph, a header, and a footer.</summary>
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

    private static Paragraph BuildParagraph(string text)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return new Paragraph(run);
    }
}
