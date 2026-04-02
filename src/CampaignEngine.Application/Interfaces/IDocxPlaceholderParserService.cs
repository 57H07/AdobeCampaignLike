using CampaignEngine.Application.DTOs.Templates;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Application-level service for extracting and validating placeholders in DOCX files.
/// Abstracts the OpenXml-dependent <c>DocxPlaceholderParser</c> so callers (controllers,
/// application services) are not coupled to Infrastructure types.
/// </summary>
public interface IDocxPlaceholderParserService
{
    /// <summary>
    /// Extracts all scalar placeholder keys from the DOCX stream and returns
    /// the keys that are <em>not</em> declared in <paramref name="manifestEntries"/>.
    ///
    /// F-307: Validation is informational only — this method never throws for
    /// undeclared placeholders; the caller decides whether to block or warn.
    /// </summary>
    /// <param name="docxStream">Readable stream containing the raw DOCX bytes.
    /// The stream is read but not closed by this method.</param>
    /// <param name="manifestEntries">Currently declared manifest entries for the template.
    /// Pass an empty collection when the template has no manifest yet.</param>
    /// <returns>
    /// Read-only list of placeholder keys found in the DOCX but absent from the manifest.
    /// Empty when all extracted keys are declared (or when the DOCX contains no placeholders).
    /// </returns>
    IReadOnlyList<string> GetUndeclaredPlaceholders(
        Stream docxStream,
        IEnumerable<PlaceholderManifestEntryDto> manifestEntries);
}
