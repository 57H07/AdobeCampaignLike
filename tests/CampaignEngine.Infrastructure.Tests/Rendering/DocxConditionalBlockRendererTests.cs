using System.IO;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// Unit tests for <see cref="DocxConditionalBlockRenderer"/>.
///
/// Covers:
/// - TASK-016-07: True condition — content between markers is kept, markers removed.
/// - TASK-016-08: False condition — content and markers are all removed.
/// - TASK-016-09: Nested {{ if }} blocks throw <see cref="TemplateRenderException"/>.
///
/// All DOCX documents are built purely in-memory; no binary files are committed.
/// </summary>
public class DocxConditionalBlockRendererTests
{
    private readonly DocxConditionalBlockRenderer _sut = new();

    // ----------------------------------------------------------------
    // TASK-016-07: True condition — content kept, markers removed
    // ----------------------------------------------------------------

    [Fact]
    public void RenderConditionals_TrueCondition_ContentKeptMarkersRemoved()
    {
        // Arrange: body = [{{ if showSection }}, "Important text", {{ end }}]
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithConditionalBlock(
            "showSection",
            new[] { "Important text" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["showSection"] = true };

        // Act
        _sut.RenderConditionals(doc, conditions);

        // Assert: only content paragraph remains, markers gone
        var paragraphs = GetBodyParagraphs(doc);
        paragraphs.Should().HaveCount(1);
        paragraphs[0].Should().Be("Important text");
    }

    [Fact]
    public void RenderConditionals_TrueCondition_MultipleContentParagraphs_AllKept()
    {
        // Arrange: body = [{{ if show }}, "Line 1", "Line 2", "Line 3", {{ end }}]
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithConditionalBlock(
            "show",
            new[] { "Line 1", "Line 2", "Line 3" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["show"] = true };

        _sut.RenderConditionals(doc, conditions);

        var paragraphs = GetBodyParagraphs(doc);
        paragraphs.Should().Equal("Line 1", "Line 2", "Line 3");
    }

    [Fact]
    public void RenderConditionals_TrueCondition_ContentBeforeAndAfterBlock_Preserved()
    {
        // Arrange: [before, {{ if key }}, inside, {{ end }}, after]
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithSurroundingContent(
            beforeText: "Before",
            conditionKey: "flag",
            innerTexts: new[] { "Inside" },
            afterText: "After");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["flag"] = true };

        _sut.RenderConditionals(doc, conditions);

        var paragraphs = GetBodyParagraphs(doc);
        paragraphs.Should().Equal("Before", "Inside", "After");
    }

    [Fact]
    public void RenderConditionals_TrueCondition_MarkerRowIsTable_ContentKept()
    {
        // Arrange: table with [{{ if key }} row, content row, {{ end }} row]
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithTableConditionalBlock(
            "showRow",
            new[] { "Table content" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["showRow"] = true };

        _sut.RenderConditionals(doc, conditions);

        var rows = GetTableRows(doc);
        rows.Should().HaveCount(1);
        rows[0].Should().Be("Table content");
    }

    // ----------------------------------------------------------------
    // TASK-016-08: False condition — content and markers removed
    // ----------------------------------------------------------------

    [Fact]
    public void RenderConditionals_FalseCondition_ContentAndMarkersRemoved()
    {
        // Arrange
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithConditionalBlock(
            "showSection",
            new[] { "Secret text" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["showSection"] = false };

        _sut.RenderConditionals(doc, conditions);

        // Assert: everything removed — body has no non-empty paragraphs
        var paragraphs = GetBodyParagraphs(doc);
        paragraphs.Should().BeEmpty();
    }

    [Fact]
    public void RenderConditionals_FalseCondition_MultipleContentParagraphs_AllRemoved()
    {
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithConditionalBlock(
            "show",
            new[] { "Line 1", "Line 2", "Line 3" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["show"] = false };

        _sut.RenderConditionals(doc, conditions);

        GetBodyParagraphs(doc).Should().BeEmpty();
    }

    [Fact]
    public void RenderConditionals_MissingKey_TreatedAsFalse_ContentRemoved()
    {
        // Business Rule 2: missing keys treated as false
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithConditionalBlock(
            "unknownKey",
            new[] { "Hidden text" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool>(); // no key at all

        _sut.RenderConditionals(doc, conditions);

        GetBodyParagraphs(doc).Should().BeEmpty();
    }

    [Fact]
    public void RenderConditionals_FalseCondition_ContentBeforeAndAfterBlock_SurroundingPreserved()
    {
        // Arrange: [before, {{ if key }}, inside, {{ end }}, after]
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithSurroundingContent(
            beforeText: "Before",
            conditionKey: "flag",
            innerTexts: new[] { "Inside" },
            afterText: "After");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["flag"] = false };

        _sut.RenderConditionals(doc, conditions);

        var paragraphs = GetBodyParagraphs(doc);
        paragraphs.Should().Equal("Before", "After");
    }

    [Fact]
    public void RenderConditionals_MultipleBlocks_EachEvaluatedIndependently()
    {
        // Arrange: two independent conditional blocks in the body
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithTwoConditionalBlocks(
            key1: "showA", inner1: "Block A",
            key2: "showB", inner2: "Block B");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool>
        {
            ["showA"] = true,
            ["showB"] = false
        };

        _sut.RenderConditionals(doc, conditions);

        var paragraphs = GetBodyParagraphs(doc);
        paragraphs.Should().Equal("Block A");
    }

    [Fact]
    public void RenderConditionals_NoConditionals_DocumentUnchanged()
    {
        // Arrange: plain document with no markers
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithParagraphs("Hello", "World");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["x"] = true };

        _sut.RenderConditionals(doc, conditions);

        GetBodyParagraphs(doc).Should().Equal("Hello", "World");
    }

    [Fact]
    public void RenderConditionals_FalseCondition_TableRow_RowRemoved()
    {
        // Arrange: table with [{{ if key }} row, content row, {{ end }} row]
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithTableConditionalBlock(
            "showRow",
            new[] { "Hidden row" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["showRow"] = false };

        _sut.RenderConditionals(doc, conditions);

        GetTableRows(doc).Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // TASK-016-09: Nested {{ if }} blocks throw exception
    // ----------------------------------------------------------------

    [Fact]
    public void RenderConditionals_NestedIfBlock_ThrowsTemplateRenderException()
    {
        // Arrange: {{ if outer }}, {{ if inner }}, content, {{ end }}, {{ end }}
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithNestedConditionals("outer", "inner");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool> { ["outer"] = true, ["inner"] = true };

        var act = () => _sut.RenderConditionals(doc, conditions);

        act.Should().Throw<TemplateRenderException>()
            .WithMessage("*Nested*if*outer*");
    }

    [Fact]
    public void RenderConditionals_NestedIfBlock_ExceptionMessageContainsOuterKey()
    {
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithNestedConditionals("myKey", "innerKey");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var conditions = new Dictionary<string, bool>();

        var act = () => _sut.RenderConditionals(doc, conditions);

        act.Should().Throw<TemplateRenderException>()
            .WithMessage("*myKey*");
    }

    // ----------------------------------------------------------------
    // Guard clauses
    // ----------------------------------------------------------------

    [Fact]
    public void RenderConditionals_NullDocument_ThrowsArgumentNullException()
    {
        var act = () => _sut.RenderConditionals(null!, new Dictionary<string, bool>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenderConditionals_NullConditions_ThrowsArgumentNullException()
    {
        using var stream = DocxConditionalBlockRendererFixtures.CreateDocxWithParagraphs("text");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var act = () => _sut.RenderConditionals(doc, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private static List<string> GetBodyParagraphs(WordprocessingDocument doc)
        => doc.MainDocumentPart!.Document!.Body!
              .Descendants<Paragraph>()
              .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)))
              .Where(t => !string.IsNullOrEmpty(t))
              .ToList();

    private static List<string> GetTableRows(WordprocessingDocument doc)
        => doc.MainDocumentPart!.Document!.Body!
              .Descendants<TableRow>()
              .Select(r => string.Concat(r.Descendants<Text>().Select(t => t.Text)))
              .Where(t => !string.IsNullOrEmpty(t))
              .ToList();
}

/// <summary>
/// In-memory DOCX factory for <see cref="DocxConditionalBlockRendererTests"/>.
/// </summary>
internal static class DocxConditionalBlockRendererFixtures
{
    /// <summary>
    /// Creates a DOCX with:
    ///   {{ if conditionKey }}
    ///   [one paragraph per innerText]
    ///   {{ end }}
    /// </summary>
    public static Stream CreateDocxWithConditionalBlock(
        string conditionKey,
        IEnumerable<string> innerTexts)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();

            body.AppendChild(BuildParagraph($"{{{{ if {conditionKey} }}}}"));
            foreach (var text in innerTexts)
                body.AppendChild(BuildParagraph(text));
            body.AppendChild(BuildParagraph("{{ end }}"));

            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with surrounding content paragraphs around a conditional block.
    /// </summary>
    public static Stream CreateDocxWithSurroundingContent(
        string beforeText,
        string conditionKey,
        IEnumerable<string> innerTexts,
        string afterText)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();

            body.AppendChild(BuildParagraph(beforeText));
            body.AppendChild(BuildParagraph($"{{{{ if {conditionKey} }}}}"));
            foreach (var text in innerTexts)
                body.AppendChild(BuildParagraph(text));
            body.AppendChild(BuildParagraph("{{ end }}"));
            body.AppendChild(BuildParagraph(afterText));

            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with two sequential independent conditional blocks.
    /// </summary>
    public static Stream CreateDocxWithTwoConditionalBlocks(
        string key1, string inner1,
        string key2, string inner2)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();

            body.AppendChild(BuildParagraph($"{{{{ if {key1} }}}}"));
            body.AppendChild(BuildParagraph(inner1));
            body.AppendChild(BuildParagraph("{{ end }}"));

            body.AppendChild(BuildParagraph($"{{{{ if {key2} }}}}"));
            body.AppendChild(BuildParagraph(inner2));
            body.AppendChild(BuildParagraph("{{ end }}"));

            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with a table where:
    ///   Row 0: {{ if conditionKey }}
    ///   Row 1..n: inner content rows
    ///   Row n+1: {{ end }}
    /// </summary>
    public static Stream CreateDocxWithTableConditionalBlock(
        string conditionKey,
        IEnumerable<string> innerTexts)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();

            var table = new Table();
            table.AppendChild(BuildTableRow($"{{{{ if {conditionKey} }}}}"));
            foreach (var text in innerTexts)
                table.AppendChild(BuildTableRow(text));
            table.AppendChild(BuildTableRow("{{ end }}"));

            body.AppendChild(table);
            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with nested conditional blocks:
    ///   {{ if outerKey }}
    ///   {{ if innerKey }}
    ///   content
    ///   {{ end }}
    ///   {{ end }}
    /// </summary>
    public static Stream CreateDocxWithNestedConditionals(string outerKey, string innerKey)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();

            body.AppendChild(BuildParagraph($"{{{{ if {outerKey} }}}}"));
            body.AppendChild(BuildParagraph($"{{{{ if {innerKey} }}}}"));
            body.AppendChild(BuildParagraph("Content"));
            body.AppendChild(BuildParagraph("{{ end }}"));
            body.AppendChild(BuildParagraph("{{ end }}"));

            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a plain DOCX with the given paragraph texts and no conditional markers.
    /// </summary>
    public static Stream CreateDocxWithParagraphs(params string[] texts)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in texts)
                body.AppendChild(BuildParagraph(text));
            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    private static Paragraph BuildParagraph(string text)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return new Paragraph(run);
    }

    private static TableRow BuildTableRow(string text)
    {
        var cell = new TableCell(new Paragraph(
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
        return new TableRow(cell);
    }
}

