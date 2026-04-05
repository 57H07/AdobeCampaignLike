using System.IO.Compression;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using DocumentFormat.OpenXml.Packaging;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Validates the structural integrity and security of an uploaded DOCX file.
///
/// US-009 (F-203): Enforces five sequential validation gates:
///   1. Extension check — only .docx accepted (case-insensitive); .docm explicitly rejected.
///   2. ZIP archive validity — file must be a valid ZIP container.
///   3. [Content_Types].xml presence — mandatory part for all OOXML packages.
///   4. WordprocessingDocument.Open — file must parse as a valid Word document.
///   5. No vbaProject.bin — macro-enabled documents are rejected.
///
/// Each failed gate throws <see cref="ValidationException"/> with a descriptive message.
/// The global exception middleware maps ValidationException to HTTP 422.
/// </summary>
public sealed class DocxValidationService : IDocxValidationService
{
    // The ZIP entry name for the OOXML content-types manifest is always at the root.
    private const string ContentTypesEntry = "[Content_Types].xml";

    // vbaProject.bin appears inside word/ when macros are present.
    // We match any entry path that ends with this file name to cover edge cases.
    private const string VbaProjectBin = "vbaProject.bin";

    /// <inheritdoc />
    public Task ValidateAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        // TASK-009-02: Extension check (case-insensitive; .docm explicitly rejected).
        ValidateExtension(fileName);

        // The remaining checks read the stream.
        // We buffer into a MemoryStream so that we can seek back to the start between checks.
        // For large files callers should gate on size before calling this method (US-010).
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        // TASK-009-03: Valid ZIP archive.
        // TASK-009-04: [Content_Types].xml present inside the ZIP.
        ValidateZipStructure(buffer);

        // TASK-009-06: No vbaProject.bin (macro detection) — done inside ZIP before parsing.
        buffer.Position = 0;
        ValidateNoMacros(buffer);

        // TASK-009-05: File opens as WordprocessingDocument.
        buffer.Position = 0;
        ValidateWordprocessingDocument(buffer);

        return Task.CompletedTask;
    }

    // ----------------------------------------------------------------
    // TASK-009-02: Extension validation
    // ----------------------------------------------------------------

    /// <summary>
    /// Enforces the .docx extension rule (case-insensitive).
    /// </summary>
    /// <exception cref="ValidationException">Thrown for any extension other than .docx.</exception>
    private static void ValidateExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            // TASK-009-07: Clear message for missing file name.
            throw new ValidationException(
                "File name is required. Only files with a .docx extension are accepted.");
        }

        var extension = Path.GetExtension(fileName);

        // Empty extension (e.g. no dot at all).
        if (string.IsNullOrEmpty(extension))
        {
            throw new ValidationException(
                $"The file '{fileName}' has no extension. Only .docx files are accepted.");
        }

        // .docm is called out explicitly per the business rule.
        if (extension.Equals(".docm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"The file '{fileName}' has a .docm extension. " +
                "Macro-enabled Word documents (.docm) are not permitted. " +
                "Please save the file as a standard .docx document.");
        }

        if (!extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"The file '{fileName}' has an unsupported extension '{extension}'. " +
                "Only .docx files are accepted.");
        }
    }

    // ----------------------------------------------------------------
    // TASK-009-03 + TASK-009-04: ZIP archive and [Content_Types].xml
    // ----------------------------------------------------------------

    /// <summary>
    /// Verifies that the stream is a valid ZIP archive and contains [Content_Types].xml.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the file is not a valid ZIP or is missing [Content_Types].xml.
    /// </exception>
    private static void ValidateZipStructure(Stream stream)
    {
        ZipArchive? archive = null;
        try
        {
            // ZipArchive throws InvalidDataException for corrupt/non-ZIP streams.
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            // TASK-009-07: Clear message for corrupt ZIP.
            throw new ValidationException(
                "The uploaded file is not a valid DOCX document. " +
                "The file could not be read as a ZIP archive, which may indicate the file is " +
                "corrupt or was not saved correctly. " +
                $"Detail: {ex.Message}");
        }

        using (archive)
        {
            // TASK-009-04: [Content_Types].xml must be present at the root.
            var hasContentTypes = archive.Entries.Any(e =>
                string.Equals(e.FullName, ContentTypesEntry, StringComparison.OrdinalIgnoreCase));

            if (!hasContentTypes)
            {
                throw new ValidationException(
                    "The uploaded file is missing the required '[Content_Types].xml' part. " +
                    "This part is mandatory for all OOXML (Office Open XML) documents. " +
                    "The file may be corrupt or was not generated by Microsoft Word.");
            }
        }
    }

    // ----------------------------------------------------------------
    // TASK-009-06: Macro detection
    // ----------------------------------------------------------------

    /// <summary>
    /// Rejects documents that contain a vbaProject.bin part (macro-enabled files).
    /// </summary>
    /// <exception cref="ValidationException">Thrown when vbaProject.bin is detected.</exception>
    private static void ValidateNoMacros(Stream stream)
    {
        // We re-open ZipArchive here; the stream has been rewound by the caller.
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var hasVba = archive.Entries.Any(e =>
            e.Name.Equals(VbaProjectBin, StringComparison.OrdinalIgnoreCase));

        if (hasVba)
        {
            throw new ValidationException(
                "The uploaded file contains a 'vbaProject.bin' part, indicating it is a " +
                "macro-enabled document. Macro-enabled documents are not permitted for security " +
                "reasons. Please remove all macros and save the file as a plain .docx document.");
        }
    }

    // ----------------------------------------------------------------
    // TASK-009-05: WordprocessingDocument.Open
    // ----------------------------------------------------------------

    /// <summary>
    /// Attempts to open the stream as a WordprocessingDocument to confirm it is a valid
    /// Word document (not merely a ZIP with a [Content_Types].xml).
    /// </summary>
    /// <exception cref="ValidationException">Thrown when the document cannot be opened.</exception>
    private static void ValidateWordprocessingDocument(Stream stream)
    {
        try
        {
            // isEditable: false — read-only open; does not modify the stream.
            using var doc = WordprocessingDocument.Open(stream, isEditable: false);

            // A valid Word document must have a MainDocumentPart.
            if (doc.MainDocumentPart is null)
            {
                throw new ValidationException(
                    "The uploaded file could not be validated as a Word document: " +
                    "the document body part (word/document.xml) is missing. " +
                    "The file may be corrupt or was not created by Microsoft Word.");
            }
        }
        catch (ValidationException)
        {
            // Re-throw our own ValidationException without wrapping.
            throw;
        }
        catch (Exception ex)
        {
            // TASK-009-07: Clear message for parse failures.
            throw new ValidationException(
                "The uploaded file could not be opened as a Word document (.docx). " +
                "The file may be corrupt, password-protected, or in an unsupported format. " +
                $"Detail: {ex.Message}");
        }
    }
}
