using System.Security;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Replaces scalar <c>{{ key }}</c> placeholders inside a
/// <see cref="WordprocessingDocument"/> with values from a recipient data
/// dictionary.
///
/// F-302 behaviour:
/// - Traverses the main document body, all HeaderParts and all FooterParts.
/// - Matches the pattern <c>{{ key }}</c> (spaces inside braces are optional,
///   spaces between <c>{{</c> / <c>}}</c> and the key name are trimmed).
/// - Keys are case-sensitive.
/// - Values are XML-escaped before insertion (prevents OpenXML injection).
/// - Missing keys are replaced with an empty string (no exception).
/// - Nested placeholders (e.g. <c>{{ {{ key }} }}</c>) are not supported and
///   are replaced literally using the outer match.
/// </summary>
public sealed class DocxPlaceholderReplacer
{
    // TASK-014-03: pattern for {{ key }} — spaces inside braces are optional,
    // key may be any non-whitespace-or-brace characters.
    private static readonly Regex PlaceholderPattern = new(
        @"\{\{\s*(?<key>[^{}\s]+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces all <c>{{ key }}</c> placeholders found in run text nodes
    /// throughout the document (body, headers, footers).
    /// </summary>
    /// <param name="doc">The open <see cref="WordprocessingDocument"/>.</param>
    /// <param name="data">
    /// Recipient data dictionary.  Keys are case-sensitive.  Missing keys
    /// produce an empty string replacement.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="doc"/> or <paramref name="data"/> is
    /// <c>null</c>.
    /// </exception>
    public void ReplaceScalars(WordprocessingDocument doc, Dictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(data);

        var mainPart = doc.MainDocumentPart;
        if (mainPart is null) return;

        // Main document body
        if (mainPart.Document?.Body is not null)
        {
            ProcessContainer(mainPart.Document.Body, data);
        }

        // All header parts
        foreach (var headerPart in mainPart.HeaderParts)
        {
            if (headerPart.Header is not null)
            {
                ProcessContainer(headerPart.Header, data);
            }
        }

        // All footer parts
        foreach (var footerPart in mainPart.FooterParts)
        {
            if (footerPart.Footer is not null)
            {
                ProcessContainer(footerPart.Footer, data);
            }
        }
    }

    // ---------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Visits every <see cref="Text"/> element inside <paramref name="container"/>
    /// and performs placeholder replacement in-place.
    /// </summary>
    private static void ProcessContainer(OpenXmlElement container, Dictionary<string, string> data)
    {
        // ToList() prevents modification-during-enumeration issues.
        foreach (var text in container.Descendants<Text>().ToList())
        {
            if (string.IsNullOrEmpty(text.Text)) continue;
            if (!text.Text.Contains("{{")) continue;

            text.Text = ReplacePlaceholders(text.Text, data);
        }
    }

    /// <summary>
    /// Applies placeholder substitution to a single text string.
    ///
    /// - TASK-014-03: uses <see cref="PlaceholderPattern"/> to find tokens.
    /// - TASK-014-04: values are XML-escaped via <see cref="SecurityElement.Escape"/>.
    /// - TASK-014-05: missing keys produce an empty string.
    /// </summary>
    private static string ReplacePlaceholders(string input, Dictionary<string, string> data)
    {
        return PlaceholderPattern.Replace(input, match =>
        {
            var key = match.Groups["key"].Value;

            // TASK-014-05: missing key → empty string (no exception)
            if (!data.TryGetValue(key, out var rawValue))
                return string.Empty;

            // TASK-014-04: XML-escape to prevent OpenXML injection.
            // SecurityElement.Escape handles &, <, >, ", ' correctly.
            return XmlEscape(rawValue);
        });
    }

    /// <summary>
    /// XML-escapes a string so it is safe to embed in an OpenXML text node.
    /// Handles: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&quot;</c>,
    /// <c>&apos;</c>.
    /// </summary>
    public static string XmlEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // SecurityElement.Escape covers &, <, >, " and '
        return SecurityElement.Escape(value) ?? value;
    }
}
