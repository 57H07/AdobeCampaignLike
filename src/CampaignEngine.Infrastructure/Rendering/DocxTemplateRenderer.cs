using CampaignEngine.Application.Interfaces;
using DocumentFormat.OpenXml.Packaging;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Implements the full DOCX rendering pipeline (F-301 through F-304):
///   1. <see cref="DocxRunMerger"/> — merges split Word XML runs.
///   2. <see cref="DocxPlaceholderReplacer"/> — replaces {{ key }} scalar placeholders.
///   3. <see cref="DocxTableCollectionRenderer"/> — expands {{ collection }} table rows.
///   4. <see cref="DocxConditionalBlockRenderer"/> — evaluates {{ if key }} blocks.
///
/// Each render call operates on an isolated in-memory copy of the source DOCX bytes;
/// the original bytes are never mutated.
/// </summary>
public sealed class DocxTemplateRenderer : IDocxTemplateRenderer
{
    private readonly DocxRunMerger _runMerger;
    private readonly DocxPlaceholderReplacer _replacer;
    private readonly DocxTableCollectionRenderer _collectionRenderer;
    private readonly DocxConditionalBlockRenderer _conditionalRenderer;

    public DocxTemplateRenderer(
        DocxRunMerger runMerger,
        DocxPlaceholderReplacer replacer,
        DocxTableCollectionRenderer collectionRenderer,
        DocxConditionalBlockRenderer conditionalRenderer)
    {
        _runMerger = runMerger;
        _replacer = replacer;
        _collectionRenderer = collectionRenderer;
        _conditionalRenderer = conditionalRenderer;
    }

    /// <inheritdoc />
    public Task<byte[]> RenderAsync(
        byte[] docxBytes,
        Dictionary<string, string> scalars,
        Dictionary<string, List<Dictionary<string, string>>> collections,
        Dictionary<string, bool> conditions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docxBytes);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(collections);
        ArgumentNullException.ThrowIfNull(conditions);

        // Work on an in-memory copy so the original bytes remain unmodified.
        using var ms = new MemoryStream();
        ms.Write(docxBytes, 0, docxBytes.Length);
        ms.Position = 0;

        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            // Step 1: Merge runs so split placeholders are recognised.
            _runMerger.MergeRuns(doc);

            // Step 2: Conditionals first (remove unwanted content before scalar replacement).
            _conditionalRenderer.RenderConditionals(doc, conditions);

            // Step 3: Expand collection table rows.
            _collectionRenderer.RenderCollections(doc, collections);

            // Step 4: Replace scalar placeholders last (after structure changes).
            _replacer.ReplaceScalars(doc, scalars);

            doc.Save();
        }

        return Task.FromResult(ms.ToArray());
    }
}
