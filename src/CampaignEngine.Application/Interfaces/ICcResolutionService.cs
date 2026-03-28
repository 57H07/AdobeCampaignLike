namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Resolves the final, validated, deduplicated CC and BCC recipient lists for a campaign send.
///
/// US-029 TASK-029-03: CC deduplication service.
///
/// Business rules:
///   BR-1: Static CC: comma-separated email list from campaign configuration.
///   BR-2: Dynamic CC: data field containing email or semicolon-separated list, per recipient.
///   BR-3: Invalid emails logged but don't fail send.
///   BR-4: Max 10 CC recipients per send (static + dynamic combined).
///   BR-5: Deduplication — the same address only receives one copy (case-insensitive).
/// </summary>
public interface ICcResolutionService
{
    /// <summary>
    /// Resolves the CC address list for a single recipient send.
    ///
    /// Combines static CC addresses (campaign-level) with dynamic CC addresses
    /// (extracted from recipient data row), validates all addresses, deduplicates,
    /// and enforces the 10-recipient cap.
    /// </summary>
    /// <param name="staticCcAddresses">
    /// Comma-separated static CC list from campaign config. May be null.
    /// </param>
    /// <param name="dynamicCcField">
    /// Field name in recipient data for dynamic CC. May be null.
    /// </param>
    /// <param name="recipientData">
    /// Recipient data row (from data source). Used to extract the dynamic CC field value.
    /// </param>
    /// <returns>Validated, deduplicated, capped list of CC email addresses.</returns>
    IReadOnlyList<string> ResolveCc(
        string? staticCcAddresses,
        string? dynamicCcField,
        IDictionary<string, object?> recipientData);

    /// <summary>
    /// Resolves the BCC address list for a single recipient send.
    ///
    /// Validates and deduplicates the static BCC list.
    /// </summary>
    /// <param name="staticBccAddresses">
    /// Comma-separated static BCC list from campaign config. May be null.
    /// </param>
    /// <returns>Validated, deduplicated list of BCC email addresses.</returns>
    IReadOnlyList<string> ResolveBcc(string? staticBccAddresses);
}
