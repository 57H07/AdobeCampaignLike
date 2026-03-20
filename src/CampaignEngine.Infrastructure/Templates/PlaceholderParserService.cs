using System.Text.RegularExpressions;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Parses Scriban template syntax to extract placeholder keys.
///
/// Recognized patterns:
///   Scalar:    {{ key }}  or  {{key}}
///   Iteration: {{ for item in collection }} ... {{ end }}
///              {{ for row in tableName }} ... {{ end }}
///
/// Business rule 1 (BR-1): Scalar uses {{ key }}, table iteration uses {{ for x in key }}.
/// </summary>
public sealed class PlaceholderParserService : IPlaceholderParserService
{
    // Matches {{ for <alias> in <collectionKey> }} — captures the collection variable name
    private static readonly Regex IterationPattern = new(
        @"\{\{\s*for\s+\w+\s+in\s+(?<key>[a-zA-Z_][a-zA-Z0-9_.]*)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches {{ key }} or {{key}} where key is a simple identifier (not a keyword like if/end/for/else)
    // Excludes Scriban keywords: if, else, end, for, in, func, ret, break, continue, while, do, case, when, null, true, false
    private static readonly Regex ScalarPattern = new(
        @"\{\{\s*(?!(?:if|else|end|for|in|func|ret|break|continue|while|do|case|when|null|true|false)\b)(?<key>[a-zA-Z_][a-zA-Z0-9_.]*)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Scriban keywords to exclude from scalar detection
    private static readonly HashSet<string> ScribanKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "elseif", "end", "for", "in", "func", "ret",
        "break", "continue", "while", "do", "case", "when",
        "null", "true", "false", "and", "or", "not",
        "import", "include", "with", "capture", "tablerow"
    };

    // Matches the loop alias: {{ for <alias> in <key> }} — captures alias and key separately
    private static readonly Regex ForLoopAliasPattern = new(
        @"\{\{\s*for\s+(?<alias>\w+)\s+in\s+(?<key>[a-zA-Z_][a-zA-Z0-9_.]*)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <inheritdoc />
    public PlaceholderExtractionResult ExtractPlaceholders(string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            return new PlaceholderExtractionResult
            {
                ScalarKeys = Array.Empty<string>(),
                IterationKeys = Array.Empty<string>(),
                AllKeys = Array.Empty<string>()
            };
        }

        // Extract iteration (table/list) keys AND collect loop aliases
        var iterationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loopAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ForLoopAliasPattern.Matches(htmlBody))
        {
            var alias = match.Groups["alias"].Value;
            var key = match.Groups["key"].Value;
            var rootKey = key.Split('.')[0];

            if (!ScribanKeywords.Contains(rootKey))
                iterationKeys.Add(rootKey);

            if (!string.IsNullOrEmpty(alias))
                loopAliases.Add(alias);
        }

        // Extract scalar keys: exclude iteration collection keys, loop aliases, and Scriban keywords.
        // Also exclude keys that start with a known loop alias (e.g., "row" from "row.product").
        var scalarKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ScalarPattern.Matches(htmlBody))
        {
            var key = match.Groups["key"].Value;
            var rootKey = key.Split('.')[0];

            // Skip Scriban keywords, iteration collection keys, and loop aliases
            if (ScribanKeywords.Contains(rootKey)) continue;
            if (iterationKeys.Contains(rootKey)) continue;
            if (loopAliases.Contains(rootKey)) continue;

            scalarKeys.Add(rootKey);
        }

        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in scalarKeys) allKeys.Add(k);
        foreach (var k in iterationKeys) allKeys.Add(k);

        return new PlaceholderExtractionResult
        {
            ScalarKeys = scalarKeys.OrderBy(k => k).ToList().AsReadOnly(),
            IterationKeys = iterationKeys.OrderBy(k => k).ToList().AsReadOnly(),
            AllKeys = allKeys.OrderBy(k => k).ToList().AsReadOnly()
        };
    }

    /// <inheritdoc />
    public ManifestValidationResult ValidateManifestCompleteness(
        string htmlBody,
        IEnumerable<PlaceholderManifestEntryDto> manifestEntries)
    {
        var extraction = ExtractPlaceholders(htmlBody);
        var templateKeys = new HashSet<string>(extraction.AllKeys, StringComparer.OrdinalIgnoreCase);
        var declaredKeys = new HashSet<string>(
            manifestEntries.Select(e => e.Key),
            StringComparer.OrdinalIgnoreCase);

        var undeclaredKeys = templateKeys.Except(declaredKeys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k)
            .ToList()
            .AsReadOnly();

        var orphanKeys = declaredKeys.Except(templateKeys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k)
            .ToList()
            .AsReadOnly();

        var isComplete = undeclaredKeys.Count == 0;

        string summary;
        if (isComplete && orphanKeys.Count == 0)
            summary = "Manifest is complete. All template placeholders are declared.";
        else if (isComplete)
            summary = $"Manifest is complete, but {orphanKeys.Count} orphan declaration(s) found: {string.Join(", ", orphanKeys)}.";
        else
            summary = $"Manifest is incomplete. {undeclaredKeys.Count} placeholder(s) not declared: {string.Join(", ", undeclaredKeys)}.";

        return new ManifestValidationResult
        {
            IsComplete = isComplete,
            UndeclaredKeys = undeclaredKeys,
            OrphanKeys = orphanKeys,
            Summary = summary
        };
    }
}
