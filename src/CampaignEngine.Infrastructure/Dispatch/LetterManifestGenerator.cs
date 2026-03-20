using System.Text;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Generates a CSV manifest file from a list of recipient letter metadata entries.
///
/// TASK-021-05: PDF metadata generation (manifest file).
///
/// Business rule BR-3: Manifest file: CSV with recipient metadata.
///
/// CSV columns:
///   SequenceInBatch, RecipientId, DisplayName, PageCount, BatchFileName
/// </summary>
public static class LetterManifestGenerator
{
    private const string Header = "SequenceInBatch,RecipientId,DisplayName,PageCount,BatchFileName";

    /// <summary>
    /// Builds a CSV manifest string from the provided entries.
    /// Returns null (no manifest) when <paramref name="entries"/> is empty.
    /// </summary>
    public static string? BuildCsv(IReadOnlyList<LetterManifestEntry> entries)
    {
        if (entries.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine(Header);

        foreach (var entry in entries)
        {
            sb.AppendLine(
                $"{entry.SequenceInBatch}," +
                $"{EscapeCsv(entry.RecipientId)}," +
                $"{EscapeCsv(entry.DisplayName)}," +
                $"{entry.PageCount}," +
                $"{EscapeCsv(entry.BatchFileName)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a CSV field by wrapping in double-quotes if it contains
    /// commas, double-quotes, or newlines (RFC 4180).
    /// </summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
