using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CampaignEngine.Infrastructure.Rendering;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// Unit tests for <see cref="DocxRunMerger"/>.
///
/// Covers:
/// - TASK-013-09: Fragmented placeholder runs are merged so the combined text
///   contains the full placeholder token.
/// - TASK-013-10: Headers and footers are traversed and their runs are merged.
/// - TASK-013-11: Smart-quote characters (U+201C / U+201D) are normalised to
///   ASCII U+0022.
/// - TASK-013-08: Bookmark elements between merged runs are preserved.
/// - Adjacent runs with *different* rPr are NOT merged.
///
/// All DOCX documents are built purely in-memory using
/// <see cref="DocxRunMergerFixtures"/>; no binary files are committed.
/// </summary>
public class DocxRunMergerTests
{
    private readonly DocxRunMerger _sut = new();

    // shorthand helpers so call sites stay readable
    private static (string, string?) R(string text, string? rpr = null) => (text, rpr);

    // ----------------------------------------------------------------
    // TASK-013-09: Fragmented placeholder — main body
    // ----------------------------------------------------------------

    [Fact]
    public void MergeRuns_TwoRunsSameRpr_AreConsolidatedIntoOne()
    {
        // Arrange — two adjacent runs with no rPr (both absent → same)
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("{{ first"), R("Name }}"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        // Act
        _sut.MergeRuns(doc);

        // Assert
        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        var runs = para.Elements<Run>().ToList();

        runs.Should().HaveCount(1, because: "two runs with identical rPr should merge");
        GetFullText(runs[0]).Should().Be("{{ firstName }}");
    }

    [Fact]
    public void MergeRuns_SplitPlaceholderThreeRuns_FullTokenPresentAfterMerge()
    {
        // Arrange — placeholder fragmented across three runs
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("{{"), R(" first"), R("Name }}"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        // Act
        _sut.MergeRuns(doc);

        // Assert — merged text should be a recognisable placeholder
        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("{{ firstName }}");
    }

    [Fact]
    public void MergeRuns_ThreeRunsSameRpr_MergedToSingleRun()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("Hello"), R(" "), R("World"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        para.Elements<Run>().Should().HaveCount(1);
        GetParagraphText(para).Should().Be("Hello World");
    }

    [Fact]
    public void MergeRuns_RunsWithDifferentRpr_AreNotMerged()
    {
        // Arrange — first run has Bold, second has no rPr
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("Bold", "bold"), R(" Normal"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        para.Elements<Run>().Should().HaveCount(2,
            because: "runs with different formatting must NOT be merged");
    }

    [Fact]
    public void MergeRuns_RunsWithSameCustomRpr_AreMerged()
    {
        // Arrange — two runs both with identical Bold rPr
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("Hello", "bold"), R(" World", "bold"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        para.Elements<Run>().Should().HaveCount(1);
        GetParagraphText(para).Should().Be("Hello World");
    }

    [Fact]
    public void MergeRuns_NullDocument_ThrowsArgumentNullException()
    {
        var act = () => _sut.MergeRuns(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MergeRuns_EmptyParagraph_DoesNotThrow()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithEmptyParagraph();
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var act = () => _sut.MergeRuns(doc);
        act.Should().NotThrow();
    }

    [Fact]
    public void MergeRuns_SingleRun_LeftUnchanged()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("Hello World"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        para.Elements<Run>().Should().HaveCount(1);
        GetParagraphText(para).Should().Be("Hello World");
    }

    // ----------------------------------------------------------------
    // TASK-013-08: Bookmark preservation
    // ----------------------------------------------------------------

    [Fact]
    public void MergeRuns_BookmarkBetweenRuns_BookmarkPreservedInParagraph()
    {
        // Arrange — bookmark sits between two compatible runs
        using var stream = DocxRunMergerFixtures.CreateDocxWithBookmarkBetweenRuns(
            runBefore: "{{ first",
            runAfter: "Name }}");

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();

        // The bookmark elements must still exist in the paragraph
        para.Descendants<BookmarkStart>().Should().HaveCount(1,
            because: "bookmarkStart must be preserved after run merge");
        para.Descendants<BookmarkEnd>().Should().HaveCount(1,
            because: "bookmarkEnd must be preserved after run merge");
    }

    [Fact]
    public void MergeRuns_BookmarkBetweenRuns_RunsAreMergedDespiteBookmark()
    {
        // The merge must cross the bookmark and still combine the compatible runs.
        using var stream = DocxRunMergerFixtures.CreateDocxWithBookmarkBetweenRuns(
            runBefore: "{{ first",
            runAfter: "Name }}");

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("{{ firstName }}",
            because: "bookmark inside split placeholder is an edge case — text must be merged");
    }

    // ----------------------------------------------------------------
    // TASK-013-11: Smart-quote normalisation
    // ----------------------------------------------------------------

    [Fact]
    public void MergeRuns_LeftDoubleQuotationMark_NormalisedToAsciiQuote()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("\u201CHello\u201D"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("\"Hello\"");
    }

    [Fact]
    public void MergeRuns_SmartQuotesInMergedRun_NormalisedAfterMerge()
    {
        // Placeholder split across runs that also contains smart quotes
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(
            R("\u201C{{ first"), R("Name }}\u201D"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("\"{{ firstName }}\"");
    }

    [Fact]
    public void MergeRuns_NoSmartQuotes_TextUnchanged()
    {
        const string plain = "Hello \"World\"";
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(R(plain));
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be(plain);
    }

    [Theory]
    [InlineData("\u201C", "\"")]
    [InlineData("\u201D", "\"")]
    [InlineData("\u201CHello\u201D World", "\"Hello\" World")]
    [InlineData("No quotes here", "No quotes here")]
    public void MergeRuns_SmartQuoteVariants_AllNormalised(string input, string expected)
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithBodyParagraph(R(input));
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var para = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be(expected);
    }

    // ----------------------------------------------------------------
    // TASK-013-10: Header traversal
    // ----------------------------------------------------------------

    [Fact]
    public void MergeRuns_HeaderWithSplitRuns_RunsMerged()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithHeader(
            R("{{ first"), R("Name }}"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var headerPart = doc.MainDocumentPart!.HeaderParts.First();
        var para = headerPart.Header.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("{{ firstName }}");
    }

    [Fact]
    public void MergeRuns_HeaderSmartQuotes_NormalisedInHeader()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithHeader(
            R("\u201CPage Header\u201D"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var headerPart = doc.MainDocumentPart!.HeaderParts.First();
        var para = headerPart.Header.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("\"Page Header\"");
    }

    // ----------------------------------------------------------------
    // TASK-013-10: Footer traversal
    // ----------------------------------------------------------------

    [Fact]
    public void MergeRuns_FooterWithSplitRuns_RunsMerged()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithFooter(
            R("{{ first"), R("Name }}"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var footerPart = doc.MainDocumentPart!.FooterParts.First();
        var para = footerPart.Footer.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("{{ firstName }}");
    }

    [Fact]
    public void MergeRuns_FooterSmartQuotes_NormalisedInFooter()
    {
        using var stream = DocxRunMergerFixtures.CreateDocxWithFooter(
            R("\u201CPage Footer\u201D"));

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var footerPart = doc.MainDocumentPart!.FooterParts.First();
        var para = footerPart.Footer.Elements<Paragraph>().First();
        GetParagraphText(para).Should().Be("\"Page Footer\"");
    }

    // ----------------------------------------------------------------
    // Multiple paragraphs
    // ----------------------------------------------------------------

    [Fact]
    public void MergeRuns_MultipleParagraphsInBody_AllParagraphsProcessed()
    {
        var para1 = new (string Text, string? RprKey)[] { R("{{ first"), R("Name }}") };
        var para2 = new (string Text, string? RprKey)[] { R("{{ last"),  R("Name }}") };
        using var stream = DocxRunMergerFixtures.CreateDocxWithMultipleParagraphs(para1, para2);

        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        _sut.MergeRuns(doc);

        var paras = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();
        paras.Should().HaveCount(2);
        GetParagraphText(paras[0]).Should().Be("{{ firstName }}");
        GetParagraphText(paras[1]).Should().Be("{{ lastName }}");
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private static string GetFullText(Run run)
        => string.Concat(run.Elements<Text>().Select(t => t.Text));

    private static string GetParagraphText(Paragraph para)
        => string.Concat(para.Elements<Run>().SelectMany(r => r.Elements<Text>()).Select(t => t.Text));
}

/// <summary>
/// In-memory DOCX factory for <see cref="DocxRunMergerTests"/>.
///
/// Documents are built using the DocumentFormat.OpenXml SDK so that no
/// binary files are committed to the repository.
///
/// The "rprKey" parameter accepts a small set of shorthand strings:
///   null  — no <c>&lt;w:rPr&gt;</c> element
///   "bold" — <c>&lt;w:rPr&gt;&lt;w:b/&gt;&lt;/w:rPr&gt;</c>
/// </summary>
internal static class DocxRunMergerFixtures
{
    /// <summary>
    /// Creates a minimal DOCX with a single paragraph whose runs are described
    /// by the <paramref name="runs"/> params array.
    /// </summary>
    public static Stream CreateDocxWithBodyParagraph(params (string Text, string? RprKey)[] runs)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(BuildParagraph(runs)));
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates a DOCX with a single empty paragraph (no runs).</summary>
    public static Stream CreateDocxWithEmptyParagraph()
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph()));
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a minimal DOCX where two body runs are separated by a
    /// bookmarkStart / bookmarkEnd pair.
    /// </summary>
    public static Stream CreateDocxWithBookmarkBetweenRuns(
        string runBefore,
        string runAfter)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();

            var para = new Paragraph(
                BuildRun(runBefore, null),
                new BookmarkStart { Id = "1", Name = "myBookmark" },
                new BookmarkEnd { Id = "1" },
                BuildRun(runAfter, null));

            mainPart.Document = new Document(new Body(para));
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with a single header part containing the given runs.
    /// </summary>
    public static Stream CreateDocxWithHeader(params (string Text, string? RprKey)[] headerRuns)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph()));

            // Add header part
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(BuildParagraph(headerRuns));
            headerPart.Header.Save();

            // Wire header reference in the section properties
            var headerRef = new HeaderReference
            {
                Type = HeaderFooterValues.Default,
                Id = mainPart.GetIdOfPart(headerPart)
            };
            var sectPr = new SectionProperties(headerRef);
            mainPart.Document.Body!.AppendChild(sectPr);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with a single footer part containing the given runs.
    /// </summary>
    public static Stream CreateDocxWithFooter(params (string Text, string? RprKey)[] footerRuns)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph()));

            // Add footer part
            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(BuildParagraph(footerRuns));
            footerPart.Footer.Save();

            // Wire footer reference in the section properties
            var footerRef = new FooterReference
            {
                Type = HeaderFooterValues.Default,
                Id = mainPart.GetIdOfPart(footerPart)
            };
            var sectPr = new SectionProperties(footerRef);
            mainPart.Document.Body!.AppendChild(sectPr);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with multiple paragraphs. Each element of
    /// <paramref name="paragraphRuns"/> is an array of (text, rprKey) pairs
    /// describing one paragraph.
    /// </summary>
    public static Stream CreateDocxWithMultipleParagraphs(
        params (string Text, string? RprKey)[][] paragraphRuns)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var runs in paragraphRuns)
            {
                body.AppendChild(BuildParagraph(runs));
            }
            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    // ---------------------------------------------------------------
    // Private builders
    // ---------------------------------------------------------------

    private static Paragraph BuildParagraph(IEnumerable<(string Text, string? RprKey)> runs)
    {
        var para = new Paragraph();
        foreach (var (text, rprKey) in runs)
        {
            para.AppendChild(BuildRun(text, rprKey));
        }
        return para;
    }

    private static Run BuildRun(string text, string? rprKey)
    {
        var run = new Run();

        if (rprKey is not null)
        {
            run.AppendChild(BuildRpr(rprKey));
        }

        var t = new Text(text)
        {
            Space = SpaceProcessingModeValues.Preserve
        };
        run.AppendChild(t);
        return run;
    }

    private static RunProperties BuildRpr(string key) => key switch
    {
        "bold" => new RunProperties(new Bold()),
        _ => new RunProperties()
    };
}
