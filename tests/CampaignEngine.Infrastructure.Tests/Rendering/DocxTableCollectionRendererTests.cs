using System.IO;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// Unit tests for <see cref="DocxTableCollectionRenderer"/>.
///
/// Covers:
/// - TASK-015-09: Collection rendering with 3-5 items — rows duplicated correctly.
/// - TASK-015-10: Empty collection — marker/end rows removed, no output rows.
/// - TASK-015-11: Missing <c>{{ end }}</c> row — throws <see cref="TemplateRenderException"/>.
///
/// All DOCX documents are built purely in-memory; no binary files are committed.
/// </summary>
public class DocxTableCollectionRendererTests
{
    private readonly DocxTableCollectionRenderer _sut = new();

    // ----------------------------------------------------------------
    // TASK-015-09: Collection rendering (3-5 items)
    // ----------------------------------------------------------------

    [Fact]
    public void RenderCollections_ThreeItems_ProducesThreeRows()
    {
        // Arrange: table with marker row, template row, end row
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            collectionKey: "lines",
            templateFields: new[] { "product", "qty" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["lines"] = new()
            {
                new() { ["product"] = "Widget A", ["qty"] = "2" },
                new() { ["product"] = "Widget B", ["qty"] = "5" },
                new() { ["product"] = "Widget C", ["qty"] = "1" }
            }
        };

        // Act
        _sut.RenderCollections(doc, collections);

        // Assert
        var rows = GetTableRowTexts(doc);
        rows.Should().HaveCount(3);
        rows[0].Should().Contain("Widget A").And.Contain("2");
        rows[1].Should().Contain("Widget B").And.Contain("5");
        rows[2].Should().Contain("Widget C").And.Contain("1");
    }

    [Fact]
    public void RenderCollections_FiveItems_ProducesFiveRows()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            "items", new[] { "name" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var items = Enumerable.Range(1, 5)
            .Select(i => new Dictionary<string, string> { ["name"] = $"Item {i}" })
            .ToList();
        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["items"] = items
        };

        _sut.RenderCollections(doc, collections);

        var rows = GetTableRowTexts(doc);
        rows.Should().HaveCount(5);
        for (int i = 0; i < 5; i++)
            rows[i].Should().Contain($"Item {i + 1}");
    }

    [Fact]
    public void RenderCollections_MarkerAndEndRowsNotInOutput()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            "products", new[] { "name" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["products"] = new() { new() { ["name"] = "Laptop" } }
        };

        _sut.RenderCollections(doc, collections);

        var rows = GetTableRowTexts(doc);
        rows.Should().NotContain(r => r.Contains("{{ products }}") || r.Contains("{{ end }}"));
    }

    [Fact]
    public void RenderCollections_ItemFieldReplacement_AllFieldsSubstituted()
    {
        // Arrange: template row has three {{ item.field }} placeholders
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            "orders", new[] { "id", "product", "price" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["orders"] = new()
            {
                new() { ["id"] = "001", ["product"] = "Keyboard", ["price"] = "79.99" }
            }
        };

        _sut.RenderCollections(doc, collections);

        var rows = GetTableRowTexts(doc);
        rows.Should().HaveCount(1);
        rows[0].Should().Contain("001").And.Contain("Keyboard").And.Contain("79.99");
    }

    [Fact]
    public void RenderCollections_MissingItemField_ReplacedWithEmptyString()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            "rows", new[] { "name", "optional" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["rows"] = new()
            {
                new() { ["name"] = "Alice" } // no "optional" field
            }
        };

        _sut.RenderCollections(doc, collections);

        var rows = GetTableRowTexts(doc);
        rows.Should().HaveCount(1);
        rows[0].Should().Contain("Alice");
        // The missing field placeholder should be gone (replaced with empty string)
        rows[0].Should().NotContain("item.optional");
    }

    [Fact]
    public void RenderCollections_RowOrderPreserved()
    {
        // Items must appear in the same order as the input list.
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            "lines", new[] { "val" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["lines"] = new()
            {
                new() { ["val"] = "First" },
                new() { ["val"] = "Second" },
                new() { ["val"] = "Third" }
            }
        };

        _sut.RenderCollections(doc, collections);

        var rows = GetTableRowTexts(doc);
        rows.Should().HaveCount(3);
        rows[0].Should().Contain("First");
        rows[1].Should().Contain("Second");
        rows[2].Should().Contain("Third");
    }

    [Fact]
    public void RenderCollections_StaticRowsBeforeAndAfter_Preserved()
    {
        // Table: static header row, marker row, template row, end row, static footer row
        using var stream = DocxCollectionFixtures.CreateDocxWithSurroundingRows(
            headerText: "Header",
            collectionKey: "data",
            templateField: "val",
            footerText: "Footer");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["data"] = new()
            {
                new() { ["val"] = "Row1" },
                new() { ["val"] = "Row2" }
            }
        };

        _sut.RenderCollections(doc, collections);

        var rows = GetTableRowTexts(doc);
        // Expect: Header, Row1, Row2, Footer
        rows.Should().HaveCount(4);
        rows[0].Should().Contain("Header");
        rows[1].Should().Contain("Row1");
        rows[2].Should().Contain("Row2");
        rows[3].Should().Contain("Footer");
    }

    // ----------------------------------------------------------------
    // TASK-015-10: Empty collection
    // ----------------------------------------------------------------

    [Fact]
    public void RenderCollections_EmptyCollection_NoOutputRows()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            "lines", new[] { "product" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["lines"] = new() // empty list
        };

        _sut.RenderCollections(doc, collections);

        // Marker, template, and end rows all removed; no output rows produced.
        GetTableRowTexts(doc).Should().BeEmpty();
    }

    [Fact]
    public void RenderCollections_MissingCollectionKey_NoOutputRows()
    {
        // Collection key not in dictionary → treated as empty.
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable(
            "unknown", new[] { "product" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>(); // no key

        _sut.RenderCollections(doc, collections);

        GetTableRowTexts(doc).Should().BeEmpty();
    }

    [Fact]
    public void RenderCollections_EmptyCollection_StaticRowsPreserved()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithSurroundingRows(
            headerText: "Header",
            collectionKey: "items",
            templateField: "val",
            footerText: "Footer");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["items"] = new() // empty
        };

        _sut.RenderCollections(doc, collections);

        var rows = GetTableRowTexts(doc);
        rows.Should().HaveCount(2);
        rows[0].Should().Contain("Header");
        rows[1].Should().Contain("Footer");
    }

    // ----------------------------------------------------------------
    // TASK-015-11: Missing {{ end }} throws TemplateRenderException
    // ----------------------------------------------------------------

    [Fact]
    public void RenderCollections_MissingEndRow_ThrowsTemplateRenderException()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithMissingEndRow("invoiceLines");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var collections = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["invoiceLines"] = new() { new() { ["product"] = "x" } }
        };

        var act = () => _sut.RenderCollections(doc, collections);

        act.Should().Throw<TemplateRenderException>()
            .WithMessage("*invoiceLines*");
    }

    [Fact]
    public void RenderCollections_MissingEndRow_ExceptionMessageContainsCollectionKey()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithMissingEndRow("myItems");
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);
        var collections = new Dictionary<string, List<Dictionary<string, string>>>();

        var act = () => _sut.RenderCollections(doc, collections);

        act.Should().Throw<TemplateRenderException>()
            .WithMessage("*Missing*end*myItems*");
    }

    // ----------------------------------------------------------------
    // Guard clauses
    // ----------------------------------------------------------------

    [Fact]
    public void RenderCollections_NullDocument_ThrowsArgumentNullException()
    {
        var act = () => _sut.RenderCollections(
            null!,
            new Dictionary<string, List<Dictionary<string, string>>>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenderCollections_NullCollections_ThrowsArgumentNullException()
    {
        using var stream = DocxCollectionFixtures.CreateDocxWithCollectionTable("k", new[] { "f" });
        using var doc = WordprocessingDocument.Open(stream, isEditable: true);

        var act = () => _sut.RenderCollections(doc, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private static List<string> GetTableRowTexts(WordprocessingDocument doc)
        => doc.MainDocumentPart!.Document!.Body!
              .Descendants<TableRow>()
              .Select(r => string.Concat(r.Descendants<Text>().Select(t => t.Text)))
              .Where(t => !string.IsNullOrEmpty(t))
              .ToList();
}

/// <summary>
/// In-memory DOCX factory for <see cref="DocxTableCollectionRendererTests"/>.
/// </summary>
internal static class DocxCollectionFixtures
{
    /// <summary>
    /// Creates a DOCX containing a single table with the collection block:
    ///   Row 0: {{ collectionKey }}
    ///   Row 1: [one cell per field: {{ item.field }}]
    ///   Row 2: {{ end }}
    /// </summary>
    public static Stream CreateDocxWithCollectionTable(
        string collectionKey,
        IEnumerable<string> templateFields)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            var table = new Table();

            // Marker row
            table.AppendChild(BuildTableRow(new[] { $"{{{{ {collectionKey} }}}}" }));

            // Template row: one cell per field
            table.AppendChild(BuildTableRow(
                templateFields.Select(f => $"{{{{ item.{f} }}}}").ToArray()));

            // End row
            table.AppendChild(BuildTableRow(new[] { "{{ end }}" }));

            body.AppendChild(table);
            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with a table that has a static header row, the collection
    /// block, and a static footer row.
    /// </summary>
    public static Stream CreateDocxWithSurroundingRows(
        string headerText,
        string collectionKey,
        string templateField,
        string footerText)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            var table = new Table();

            table.AppendChild(BuildTableRow(new[] { headerText }));
            table.AppendChild(BuildTableRow(new[] { $"{{{{ {collectionKey} }}}}" }));
            table.AppendChild(BuildTableRow(new[] { $"{{{{ item.{templateField} }}}}" }));
            table.AppendChild(BuildTableRow(new[] { "{{ end }}" }));
            table.AppendChild(BuildTableRow(new[] { footerText }));

            body.AppendChild(table);
            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a DOCX with a collection marker row and a template row but no
    /// end row — used to test the missing-<c>{{ end }}</c> error path.
    /// </summary>
    public static Stream CreateDocxWithMissingEndRow(string collectionKey)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms,
            WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            var table = new Table();

            table.AppendChild(BuildTableRow(new[] { $"{{{{ {collectionKey} }}}}" }));
            table.AppendChild(BuildTableRow(new[] { "{{ item.product }}" }));
            // Intentionally no {{ end }} row

            body.AppendChild(table);
            mainPart.Document = new Document(body);
        }
        ms.Position = 0;
        return ms;
    }

    private static TableRow BuildTableRow(string[] cellTexts)
    {
        var row = new TableRow();
        foreach (var text in cellTexts)
        {
            var cell = new TableCell(new Paragraph(
                new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            row.AppendChild(cell);
        }
        return row;
    }
}
