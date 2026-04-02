using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Extracts all scalar <c>{{ key }}</c> placeholder keys from a
/// <see cref="WordprocessingDocument"/>, scanning the main body, headers,
/// and footers.
///
/// F-306 behaviour:
/// - Traversal runs after run merging so that placeholders split across Word
///   XML runs are already reunited into single text nodes.
/// - Collection markers (<c>{{ collection_key }}</c> rows where the key is a
///   plain identifier with no dots and no reserved words) are excluded via the
///   same detection used by <see cref="DocxTableCollectionRenderer"/>.
/// - Conditional markers (<c>{{ if key }}</c>, <c>{{ end }}</c>) are excluded.
/// - Item placeholders (<c>{{ item.field }}</c>) are included as-is (key =
///   <c>item.field</c>).
/// - The returned list contains each unique key exactly once (deduplicated,
///   in first-seen order).
/// </summary>
public sealed class DocxPlaceholderParser
{
    // TASK-017-03: Matches any {{ key }} token in a text node.
    // Capture group "key" is the trimmed inner content.
    private static readonly Regex TokenPattern = new(
        @"\{\{\s*(?<key>[^{}\r\n]+?)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Filters out {{ end }}.
    private static readonly Regex EndPattern = new(
        @"^end$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Filters out {{ if something }}.
    private static readonly Regex IfPattern = new(
        @"^if\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Detects a standalone paragraph/row that contains only a single
    // {{ plain_identifier }} — this is a collection marker, not a scalar
    // placeholder (plain identifier: no spaces inside, no dot, not "end").
    private static readonly Regex CollectionMarkerPattern = new(
        @"^\s*\{\{\s*(?<key>[^{}\s.]+)\s*\}\}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------

    /// <summary>
    /// Extracts all scalar placeholder keys from the document.
    /// </summary>
    /// <param name="doc">
    /// An open <see cref="WordprocessingDocument"/> whose runs have already
    /// been merged by <see cref="DocxRunMerger"/>.
    /// </param>
    /// <returns>
    /// Deduplicated list of placeholder keys in first-seen order.
    /// Collection markers, <c>{{ if … }}</c>, and <c>{{ end }}</c> are excluded.
    /// Item placeholders (<c>{{ item.field }}</c>) are included.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="doc"/> is <c>null</c>.
    /// </exception>
    public List<string> ExtractPlaceholders(WordprocessingDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        // TASK-017-05: Use a linked set to deduplicate while preserving insertion order.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        void Collect(string key)
        {
            if (seen.Add(key))
                result.Add(key);
        }

        var mainPart = doc.MainDocumentPart;
        if (mainPart is null) return result;

        // TASK-017-04: Scan main body.
        if (mainPart.Document?.Body is not null)
            CollectFromContainer(mainPart.Document.Body, Collect);

        // TASK-017-04: Scan all header parts.
        foreach (var headerPart in mainPart.HeaderParts)
        {
            if (headerPart.Header is not null)
                CollectFromContainer(headerPart.Header, Collect);
        }

        // TASK-017-04: Scan all footer parts.
        foreach (var footerPart in mainPart.FooterParts)
        {
            if (footerPart.Footer is not null)
                CollectFromContainer(footerPart.Footer, Collect);
        }

        return result;
    }

    // ---------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Walks all paragraphs and text nodes inside <paramref name="container"/>,
    /// calling <paramref name="collect"/> for each valid scalar key.
    /// </summary>
    private static void CollectFromContainer(OpenXmlElement container, Action<string> collect)
    {
        foreach (var para in container.Descendants<Paragraph>())
        {
            // Determine the full text of the paragraph so we can detect
            // collection markers (standalone {{ identifier }} paragraphs).
            var paraText = GetParagraphText(para);

            // TASK-017-03: If this paragraph is a collection marker, skip it entirely.
            if (IsCollectionMarkerParagraph(paraText))
                continue;

            // Scan each Text node in the paragraph for {{ … }} tokens.
            foreach (var textNode in para.Descendants<Text>())
            {
                if (string.IsNullOrEmpty(textNode.Text)) continue;
                if (!textNode.Text.Contains("{{")) continue;

                foreach (Match m in TokenPattern.Matches(textNode.Text))
                {
                    var key = m.Groups["key"].Value; // already trimmed by regex

                    // TASK-017-03: Exclude {{ end }} and {{ if condition }}.
                    if (EndPattern.IsMatch(key)) continue;
                    if (IfPattern.IsMatch(key)) continue;

                    collect(key);
                }
            }
        }
    }

    /// <summary>
    /// Returns the concatenated text of all <see cref="Text"/> descendants of
    /// the paragraph, used for collection-marker detection.
    /// </summary>
    private static string GetParagraphText(Paragraph para)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in para.Descendants<Text>())
            sb.Append(t.Text);
        return sb.ToString();
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="text"/> represents a standalone
    /// collection marker paragraph — i.e. the whole paragraph content is a
    /// single <c>{{ plain_identifier }}</c> token (no spaces, no dot, not "end",
    /// not prefixed with "if ").
    /// </summary>
    private static bool IsCollectionMarkerParagraph(string text)
    {
        var m = CollectionMarkerPattern.Match(text);
        if (!m.Success) return false;

        var key = m.Groups["key"].Value;

        // "end" is already a special marker, not a collection key.
        if (EndPattern.IsMatch(key)) return false;

        // "if …" cannot appear inside a plain identifier (space not allowed),
        // so no further check needed here.

        return true;
    }
}
