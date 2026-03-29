using System.IO.Compression;
using System.Text;
using CampaignEngine.Application.Services;
using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Unit tests for <see cref="DocxValidationService"/>.
///
/// US-009 (F-203): Each test exercises a single validation rule and verifies that
/// <see cref="ValidationException"/> is thrown with an informative message,
/// or that a valid DOCX passes without error.
///
/// Fixture DOCX streams are built programmatically from in-memory ZIP archives to
/// avoid committing binary files. A minimal but structurally valid DOCX requires:
///   - [Content_Types].xml
///   - word/document.xml
///   - _rels/.rels
///   - word/_rels/document.xml.rels
/// The <see cref="DocxFixtures"/> helper encapsulates this construction.
/// </summary>
public class DocxValidationServiceTests
{
    private readonly DocxValidationService _sut = new();

    // ----------------------------------------------------------------
    // Happy-path: valid DOCX passes all gates
    // ----------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_ValidDocx_DoesNotThrow()
    {
        // Arrange
        using var stream = DocxFixtures.CreateValidDocx();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docx", stream);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("TEMPLATE.DOCX")]
    [InlineData("Template.DocX")]
    [InlineData("my-letter.DOCX")]
    public async Task ValidateAsync_DocxExtensionCaseInsensitive_DoesNotThrow(string fileName)
    {
        // Arrange
        using var stream = DocxFixtures.CreateValidDocx();

        // Act
        var act = async () => await _sut.ValidateAsync(fileName, stream);

        // Assert — extension check is case-insensitive (Business Rule 1)
        await act.Should().NotThrowAsync();
    }

    // ----------------------------------------------------------------
    // TASK-009-02: Extension validation
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("template.pdf")]
    [InlineData("template.docm")]
    [InlineData("template.doc")]
    [InlineData("template.odt")]
    [InlineData("template")]
    [InlineData("template.")]
    public async Task ValidateAsync_WrongExtension_ThrowsValidationException(string fileName)
    {
        // Arrange
        using var stream = DocxFixtures.CreateValidDocx();

        // Act
        var act = async () => await _sut.ValidateAsync(fileName, stream);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ValidateAsync_DocmExtension_ThrowsWithDocmMessage()
    {
        // Arrange — .docm is explicitly called out (Business Rule 2)
        using var stream = DocxFixtures.CreateValidDocx();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docm", stream);

        // Assert — message must mention .docm
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Message.Should().Contain(".docm");
    }

    [Fact]
    public async Task ValidateAsync_NullFileName_ThrowsValidationException()
    {
        // Arrange
        using var stream = DocxFixtures.CreateValidDocx();

        // Act
        var act = async () => await _sut.ValidateAsync(null!, stream);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ValidateAsync_EmptyFileName_ThrowsValidationException()
    {
        // Arrange
        using var stream = DocxFixtures.CreateValidDocx();

        // Act
        var act = async () => await _sut.ValidateAsync(string.Empty, stream);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----------------------------------------------------------------
    // TASK-009-03: ZIP archive validation
    // ----------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_CorruptZip_ThrowsValidationException()
    {
        // Arrange — not a ZIP at all; just random bytes
        using var stream = DocxFixtures.CreateCorruptZip();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docx", stream);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Message.Should().Contain("ZIP", Exactly.Once());
    }

    [Fact]
    public async Task ValidateAsync_TruncatedZip_ThrowsValidationException()
    {
        // Arrange — valid DOCX truncated to first 20 bytes
        using var stream = DocxFixtures.CreateTruncatedZip();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docx", stream);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----------------------------------------------------------------
    // TASK-009-04: [Content_Types].xml presence
    // ----------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_MissingContentTypesXml_ThrowsValidationException()
    {
        // Arrange — valid ZIP but [Content_Types].xml is omitted
        using var stream = DocxFixtures.CreateZipMissingContentTypes();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docx", stream);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Message.Should().Contain("[Content_Types].xml");
    }

    // ----------------------------------------------------------------
    // TASK-009-05: WordprocessingDocument.Open
    // ----------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_ZipWithContentTypesButNotValidWord_ThrowsValidationException()
    {
        // Arrange — has [Content_Types].xml but does NOT have a valid MainDocumentPart
        using var stream = DocxFixtures.CreateZipWithContentTypesOnly();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docx", stream);

        // Assert — WordprocessingDocument.Open should fail or MainDocumentPart missing
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----------------------------------------------------------------
    // TASK-009-06: vbaProject.bin detection
    // ----------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_ContainsVbaProjectBin_ThrowsValidationException()
    {
        // Arrange — structurally valid DOCX but includes word/vbaProject.bin
        using var stream = DocxFixtures.CreateDocxWithVba();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docx", stream);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Message.Should().Contain("vbaProject.bin");
    }

    [Fact]
    public async Task ValidateAsync_VbaInSubDirectory_ThrowsValidationException()
    {
        // Arrange — vbaProject.bin nested under word/
        using var stream = DocxFixtures.CreateDocxWithVbaInSubdir();

        // Act
        var act = async () => await _sut.ValidateAsync("template.docx", stream);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----------------------------------------------------------------
    // TASK-009-07: Error message quality
    // ----------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_CorruptZip_ErrorMessageDescribesZipFailure()
    {
        using var stream = DocxFixtures.CreateCorruptZip();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.ValidateAsync("template.docx", stream));

        ex.Message.Should().NotBeNullOrWhiteSpace();
        ex.Message.Length.Should().BeGreaterThan(20,
            because: "error messages must be descriptive");
    }

    [Fact]
    public async Task ValidateAsync_MissingContentTypes_ErrorMessageNamesThePart()
    {
        using var stream = DocxFixtures.CreateZipMissingContentTypes();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.ValidateAsync("template.docx", stream));

        ex.Message.Should().Contain("[Content_Types].xml");
    }

    [Fact]
    public async Task ValidateAsync_VbaDetected_ErrorMessageMentionsMacros()
    {
        using var stream = DocxFixtures.CreateDocxWithVba();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.ValidateAsync("template.docx", stream));

        // Message should explain the security reason, not just name the file.
        ex.Message.Should().ContainAny("macro", "vbaProject.bin");
    }
}

/// <summary>
/// Programmatic DOCX fixture factory.
///
/// All fixtures are constructed in-memory using <see cref="ZipArchive"/> so that
/// no binary test assets need to be committed to the repository.
///
/// Minimal valid DOCX structure (OOXML spec):
/// <code>
///   [Content_Types].xml           — MIME type registry
///   _rels/.rels                   — package relationships
///   word/document.xml             — main body
///   word/_rels/document.xml.rels  — part relationships
/// </code>
/// </summary>
internal static class DocxFixtures
{
    // Minimal [Content_Types].xml for a Word document.
    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml"
            ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    // Minimal _rels/.rels pointing to word/document.xml.
    private const string RelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1"
            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
            Target="word/document.xml"/>
        </Relationships>
        """;

    // Minimal word/document.xml — empty body.
    private const string DocumentXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:wpc="http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas"
                    xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p><w:r><w:t>Hello</w:t></w:r></w:p>
          </w:body>
        </w:document>
        """;

    // Minimal word/_rels/document.xml.rels — no external references.
    private const string DocumentRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        </Relationships>
        """;

    /// <summary>Creates a structurally valid minimal DOCX stream.</summary>
    public static MemoryStream CreateValidDocx()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(zip, "_rels/.rels", RelsXml);
            WriteEntry(zip, "word/document.xml", DocumentXml);
            WriteEntry(zip, "word/_rels/document.xml.rels", DocumentRelsXml);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates a stream of random bytes — not a ZIP archive.</summary>
    public static MemoryStream CreateCorruptZip()
    {
        var bytes = new byte[512];
        Random.Shared.NextBytes(bytes);
        // Overwrite any accidental ZIP magic numbers.
        bytes[0] = 0xFF;
        bytes[1] = 0xFE;
        return new MemoryStream(bytes);
    }

    /// <summary>Creates a valid ZIP truncated to its first 20 bytes.</summary>
    public static MemoryStream CreateTruncatedZip()
    {
        using var full = CreateValidDocx();
        var truncated = full.ToArray()[..20];
        return new MemoryStream(truncated);
    }

    /// <summary>Creates a ZIP that omits the [Content_Types].xml entry.</summary>
    public static MemoryStream CreateZipMissingContentTypes()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Intentionally omit [Content_Types].xml.
            WriteEntry(zip, "_rels/.rels", RelsXml);
            WriteEntry(zip, "word/document.xml", DocumentXml);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Creates a ZIP that has [Content_Types].xml but lacks the parts needed for
    /// WordprocessingDocument.Open to succeed (no MainDocumentPart).
    /// </summary>
    public static MemoryStream CreateZipWithContentTypesOnly()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Content types declares a main part but the part file is absent.
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
            // Provide only the package-level rels — no word/ parts at all.
            WriteEntry(zip, "_rels/.rels", RelsXml);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates a valid DOCX structure but adds a vbaProject.bin at the root.</summary>
    public static MemoryStream CreateDocxWithVba()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(zip, "_rels/.rels", RelsXml);
            WriteEntry(zip, "word/document.xml", DocumentXml);
            WriteEntry(zip, "word/_rels/document.xml.rels", DocumentRelsXml);
            // Simulate a macro-enabled document by adding vbaProject.bin.
            WriteEntry(zip, "vbaProject.bin", "binary-vba-content");
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates a valid DOCX with vbaProject.bin nested inside word/.</summary>
    public static MemoryStream CreateDocxWithVbaInSubdir()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(zip, "_rels/.rels", RelsXml);
            WriteEntry(zip, "word/document.xml", DocumentXml);
            WriteEntry(zip, "word/_rels/document.xml.rels", DocumentRelsXml);
            // Nested path — the service checks entry.Name (not FullName) for the file name.
            WriteEntry(zip, "word/vbaProject.bin", "binary-vba-content");
        }
        ms.Position = 0;
        return ms;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
