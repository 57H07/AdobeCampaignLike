using System.Text.RegularExpressions;
using CampaignEngine.Domain.Exceptions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Evaluates <c>{{ if condition_key }}...{{ end }}</c> blocks inside a
/// <see cref="WordprocessingDocument"/> and removes or keeps the enclosed
/// content based on a boolean conditions dictionary.
///
/// F-304 behaviour:
/// - <c>{{ if condition_key }}</c> and <c>{{ end }}</c> each occupy a
///   dedicated paragraph or table row.
/// - If the condition value is <c>true</c> the enclosed content is kept and
///   the marker elements are removed.
/// - If the condition value is <c>false</c> (or the key is missing) the
///   marker elements AND the enclosed content are all removed.
/// - No <c>{{ else }}</c> support.
/// - Nested <c>{{ if }}</c> blocks are detected and throw
///   <see cref="TemplateRenderException"/>.
/// - Only the main document body is processed (not headers/footers).
/// </summary>
public sealed class DocxConditionalBlockRenderer
{
    // TASK-016-03: Pattern for {{ if key }} — spaces inside braces are optional.
    private static readonly Regex IfPattern = new(
        @"^\s*\{\{\s*if\s+(?<key>[^{}\s]+)\s*\}\}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // TASK-016-03: Pattern for {{ end }} — spaces inside braces are optional.
    private static readonly Regex EndPattern = new(
        @"^\s*\{\{\s*end\s*\}\}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Evaluates all conditional blocks in the main document body.
    /// </summary>
    /// <param name="doc">The open <see cref="WordprocessingDocument"/>.</param>
    /// <param name="conditions">
    /// Boolean conditions dictionary.  Keys are case-sensitive.  Missing keys
    /// are treated as <c>false</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="doc"/> or <paramref name="conditions"/> is
    /// <c>null</c>.
    /// </exception>
    /// <exception cref="TemplateRenderException">
    /// Thrown when nested <c>{{ if }}</c> blocks are detected.
    /// </exception>
    public void RenderConditionals(WordprocessingDocument doc, Dictionary<string, bool> conditions)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(conditions);

        var mainPart = doc.MainDocumentPart;
        if (mainPart?.Document?.Body is null) return;

        ProcessBody(mainPart.Document.Body, conditions);
    }

    // ---------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Processes all top-level block elements (paragraphs and table rows) in
    /// the document body, finding conditional blocks and evaluating them.
    /// </summary>
    private static void ProcessBody(Body body, Dictionary<string, bool> conditions)
    {
        // Collect the flat list of "block-level" elements we care about:
        // paragraphs at the body level, and table rows inside body-level tables.
        // We process them as a linear sequence so we can detect nesting.
        var blocks = CollectBlocks(body);

        // TASK-016-04/05/06: linear scan — find {{ if }} markers and process blocks.
        var toRemove = new List<OpenXmlElement>();
        int i = 0;
        while (i < blocks.Count)
        {
            var block = blocks[i];
            var ifKey = ExtractIfKey(block);
            if (ifKey is null)
            {
                i++;
                continue;
            }

            // We found a {{ if key }} marker.
            // TASK-016-04: scan forward for matching {{ end }}, detecting nested ifs.
            int depth = 1;
            int j = i + 1;
            while (j < blocks.Count && depth > 0)
            {
                var inner = blocks[j];
                if (ExtractIfKey(inner) is not null)
                {
                    // TASK-016-04: nested {{ if }} detected — throw with clear message.
                    throw new TemplateRenderException(
                        $"Nested {{{{ if }}}} blocks are not supported (found inside '{{{{ if {ifKey} }}}}').");
                }
                if (IsEndMarker(inner))
                {
                    depth--;
                }
                j++;
            }

            // j now points one past the {{ end }} block (or past the list end).
            int endIndex = j - 1; // index of the {{ end }} block

            // Determine whether condition is true. Missing keys → false (Business Rule 2).
            bool conditionValue = conditions.TryGetValue(ifKey, out var val) && val;

            if (conditionValue)
            {
                // TASK-016-06: condition true — keep content, remove only markers.
                toRemove.Add(block);         // {{ if key }} marker
                toRemove.Add(blocks[endIndex]); // {{ end }} marker
            }
            else
            {
                // TASK-016-05: condition false — remove markers AND all content between.
                for (int k = i; k <= endIndex; k++)
                {
                    toRemove.Add(blocks[k]);
                }
            }

            i = endIndex + 1;
        }

        // Remove collected elements from the document.
        foreach (var elem in toRemove)
        {
            elem.Remove();
        }
    }

    /// <summary>
    /// Collects all block-level elements (paragraphs and table rows) from the
    /// body in document order. This flattens the structure into a linear
    /// sequence for marker scanning.
    /// </summary>
    private static List<OpenXmlElement> CollectBlocks(Body body)
    {
        var result = new List<OpenXmlElement>();
        CollectBlocksRecursive(body, result);
        return result;
    }

    private static void CollectBlocksRecursive(OpenXmlElement container, List<OpenXmlElement> result)
    {
        foreach (var child in container.ChildElements)
        {
            if (child is Paragraph para)
            {
                result.Add(para);
            }
            else if (child is Table table)
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    // Check if the row itself is a marker; if so add the row.
                    // Otherwise check inside cells for nested tables.
                    result.Add(row);
                }
            }
            else if (child is SectionProperties)
            {
                // Skip section properties — not a content block.
            }
        }
    }

    // ---------------------------------------------------------------
    // Marker detection helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns the condition key if <paramref name="element"/> is a
    /// <c>{{ if key }}</c> marker paragraph or table row; otherwise
    /// <c>null</c>.
    /// </summary>
    private static string? ExtractIfKey(OpenXmlElement element)
    {
        var text = GetElementText(element);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = IfPattern.Match(text);
        return match.Success ? match.Groups["key"].Value : null;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="element"/> is a <c>{{ end }}</c>
    /// marker paragraph or table row.
    /// </summary>
    private static bool IsEndMarker(OpenXmlElement element)
    {
        var text = GetElementText(element);
        return !string.IsNullOrWhiteSpace(text) && EndPattern.IsMatch(text);
    }

    /// <summary>
    /// Concatenates all <see cref="Text"/> node content from an element.
    /// </summary>
    private static string GetElementText(OpenXmlElement element)
        => string.Concat(element.Descendants<Text>().Select(t => t.Text));
}
