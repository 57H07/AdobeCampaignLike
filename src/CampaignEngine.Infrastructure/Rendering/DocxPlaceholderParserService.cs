using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using DocumentFormat.OpenXml.Packaging;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Infrastructure implementation of <see cref="IDocxPlaceholderParserService"/>.
/// Opens a DOCX stream via OpenXml and delegates placeholder extraction to
/// <see cref="DocxPlaceholderParser"/>, then computes the set of keys absent
/// from the declared manifest entries.
/// </summary>
internal sealed class DocxPlaceholderParserService : IDocxPlaceholderParserService
{
    private readonly DocxPlaceholderParser _parser;

    public DocxPlaceholderParserService(DocxPlaceholderParser parser)
    {
        _parser = parser;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetUndeclaredPlaceholders(
        Stream docxStream,
        IEnumerable<PlaceholderManifestEntryDto> manifestEntries)
    {
        ArgumentNullException.ThrowIfNull(docxStream);
        ArgumentNullException.ThrowIfNull(manifestEntries);

        var declaredKeys = new HashSet<string>(
            manifestEntries.Select(e => e.Key),
            StringComparer.Ordinal);

        // US-018: Open DOCX in read-only mode to avoid locking or modifying the stream.
        using var doc = WordprocessingDocument.Open(docxStream, isEditable: false);

        var extracted = _parser.ExtractPlaceholders(doc);

        return extracted
            .Where(key => !declaredKeys.Contains(key))
            .ToList()
            .AsReadOnly();
    }
}
