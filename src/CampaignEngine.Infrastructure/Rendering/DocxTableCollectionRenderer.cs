using System.Text.RegularExpressions;
using CampaignEngine.Domain.Exceptions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Renders collection placeholders inside a <see cref="WordprocessingDocument"/>
/// by duplicating template table rows once per item in the collection.
///
/// F-303 behaviour:
/// - A marker row containing only <c>{{ collection_key }}</c> identifies the
///   start of a collection block.
/// - The immediately following row is the template row and may contain
///   <c>{{ item.field }}</c> placeholders.
/// - A row containing only <c>{{ end }}</c> marks the end of the block.
/// - The engine duplicates the template row once per item, replacing
///   <c>{{ item.field }}</c> with the corresponding item field value.
/// - The marker row and the end row are removed from the output.
/// - Missing <c>{{ end }}</c> throws <see cref="TemplateRenderException"/>.
/// - Empty collections: marker and end rows are removed, no output rows produced.
/// - Collection keys and item field names are case-sensitive.
/// - Only table-row duplication is supported (no paragraph-level loops).
/// </summary>
public sealed class DocxTableCollectionRenderer
{
    // TASK-015-03: Pattern matching a standalone {{ collection_key }} row.
    // The key must not be "end", "if", or contain spaces — it's a plain identifier.
    private static readonly Regex CollectionMarkerPattern = new(
        @"^\s*\{\{\s*(?<key>[^{}\s]+)\s*\}\}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Reuse the same {{ end }} pattern as DocxConditionalBlockRenderer.
    private static readonly Regex EndPattern = new(
        @"^\s*\{\{\s*end\s*\}\}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Pattern for {{ item.field }} placeholders inside template rows.
    private static readonly Regex ItemFieldPattern = new(
        @"\{\{\s*item\.(?<field>[^{}\s]+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Renders all collection blocks in every table in the main document body.
    /// </summary>
    /// <param name="doc">The open <see cref="WordprocessingDocument"/>.</param>
    /// <param name="collections">
    /// Dictionary mapping collection keys to their item lists.  Each item is a
    /// <see cref="Dictionary{TKey,TValue}"/> of field name → string value.
    /// Keys and field names are case-sensitive.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="doc"/> or <paramref name="collections"/> is
    /// <c>null</c>.
    /// </exception>
    /// <exception cref="TemplateRenderException">
    /// Thrown when a collection marker row has no matching <c>{{ end }}</c> row
    /// in the same table.
    /// </exception>
    public void RenderCollections(
        WordprocessingDocument doc,
        Dictionary<string, List<Dictionary<string, string>>> collections)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(collections);

        var mainPart = doc.MainDocumentPart;
        if (mainPart?.Document?.Body is null) return;

        // Process each table independently — collection blocks cannot span tables.
        foreach (var table in mainPart.Document.Body.Descendants<Table>().ToList())
        {
            ProcessTable(table, collections);
        }
    }

    // ---------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Scans all rows in <paramref name="table"/> and processes any collection
    /// blocks found (marker → template row(s) → end).
    /// </summary>
    private static void ProcessTable(
        Table table,
        Dictionary<string, List<Dictionary<string, string>>> collections)
    {
        // TASK-015-03: collect rows into a list so we can manipulate indices.
        var rows = table.Elements<TableRow>().ToList();

        // We'll build up a set of rows to remove and rows to insert.
        // Process from the back to the front so index arithmetic stays stable
        // when we splice rows in or out.
        // Instead, collect operations and apply them after the scan.

        var operations = new List<(int markerIndex, int templateIndex, int endIndex, string key)>();

        int i = 0;
        while (i < rows.Count)
        {
            var collectionKey = ExtractCollectionKey(rows[i]);
            if (collectionKey is null || collectionKey == "end" || collectionKey.StartsWith("if", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            // TASK-015-04: find the matching {{ end }} row.
            int endIndex = -1;
            for (int j = i + 2; j < rows.Count; j++) // template row is i+1, end is i+2 at minimum
            {
                if (IsEndRow(rows[j]))
                {
                    endIndex = j;
                    break;
                }
            }

            // TASK-015-08: missing {{ end }} → throw TemplateRenderException.
            if (endIndex == -1)
            {
                throw new TemplateRenderException(
                    $"Missing {{{{ end }}}} for collection '{collectionKey}'.");
            }

            // Template row is immediately after the marker row.
            int templateIndex = i + 1;

            operations.Add((i, templateIndex, endIndex, collectionKey));

            // Skip past the end row for the next outer loop iteration.
            i = endIndex + 1;
        }

        // Apply operations in reverse order so indices remain valid.
        for (int op = operations.Count - 1; op >= 0; op--)
        {
            var (markerIndex, templateIndex, endIndex, key) = operations[op];
            ExpandCollection(table, rows, markerIndex, templateIndex, endIndex, key, collections);
        }
    }

    /// <summary>
    /// Expands a single collection block:
    /// 1. Clones the template row once per item, replacing <c>{{ item.field }}</c>.
    /// 2. Inserts cloned rows after the end row position.
    /// 3. Removes the marker row, template row, and end row.
    /// </summary>
    private static void ExpandCollection(
        Table table,
        List<TableRow> allRows,
        int markerIndex,
        int templateIndex,
        int endIndex,
        string key,
        Dictionary<string, List<Dictionary<string, string>>> collections)
    {
        var templateRow = allRows[templateIndex];
        var endRow = allRows[endIndex];

        // TASK-015-05: get items (empty list if key missing).
        collections.TryGetValue(key, out var items);
        items ??= new List<Dictionary<string, string>>();

        // TASK-015-05/06: clone and fill one row per item, inserting after the end row.
        // We insert in reverse so the final order matches the item order.
        for (int itemIdx = items.Count - 1; itemIdx >= 0; itemIdx--)
        {
            var item = items[itemIdx];

            // Clone the template row deeply to preserve all formatting.
            var clonedRow = (TableRow)templateRow.CloneNode(deep: true);

            // TASK-015-06: replace {{ item.field }} placeholders in all Text nodes.
            foreach (var textNode in clonedRow.Descendants<Text>().ToList())
            {
                if (string.IsNullOrEmpty(textNode.Text) || !textNode.Text.Contains("{{"))
                    continue;

                textNode.Text = ItemFieldPattern.Replace(textNode.Text, match =>
                {
                    var field = match.Groups["field"].Value;
                    return item.TryGetValue(field, out var val) ? (val ?? string.Empty) : string.Empty;
                });
            }

            // Insert immediately after the end row.
            endRow.InsertAfterSelf(clonedRow);
        }

        // TASK-015-07: remove marker row, template row, and end row.
        // Remove in a defined order; note removing doesn't affect siblings' identity.
        allRows[markerIndex].Remove();
        allRows[templateIndex].Remove();
        allRows[endIndex].Remove();
    }

    // ---------------------------------------------------------------
    // Marker detection helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns the collection key if the row contains only a
    /// <c>{{ collection_key }}</c> marker; otherwise <c>null</c>.
    /// </summary>
    private static string? ExtractCollectionKey(TableRow row)
    {
        var text = GetRowText(row);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = CollectionMarkerPattern.Match(text);
        if (!match.Success) return null;

        var key = match.Groups["key"].Value;

        // Exclude reserved keywords so we don't mis-identify {{ end }} or {{ if x }}.
        if (key == "end") return null;

        return key;
    }

    /// <summary>
    /// Returns <c>true</c> if the row is a <c>{{ end }}</c> marker row.
    /// </summary>
    private static bool IsEndRow(TableRow row)
    {
        var text = GetRowText(row);
        return !string.IsNullOrWhiteSpace(text) && EndPattern.IsMatch(text);
    }

    /// <summary>
    /// Concatenates all <see cref="Text"/> node content within a table row.
    /// </summary>
    private static string GetRowText(TableRow row)
        => string.Concat(row.Descendants<Text>().Select(t => t.Text));
}
