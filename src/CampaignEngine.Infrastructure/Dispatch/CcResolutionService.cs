using CampaignEngine.Application.Interfaces;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Resolves, validates, and deduplicates CC and BCC address lists for campaign sends.
///
/// US-029 TASK-029-03: CC deduplication service.
///
/// Business rules enforced:
///   BR-1: Static CC comma-separated from campaign config.
///   BR-2: Dynamic CC from per-recipient data field (semicolon or comma separated).
///   BR-3: Invalid emails logged but don't fail send (delegated to IEmailValidationService).
///   BR-4: Max 10 CC recipients per send (static + dynamic combined), excess addresses logged.
///   BR-5: Case-insensitive deduplication — same address receives only one copy.
/// </summary>
public sealed class CcResolutionService : ICcResolutionService
{
    private const int MaxCcRecipientsPerSend = 10;

    private readonly IEmailValidationService _emailValidation;
    private readonly IAppLogger<CcResolutionService> _logger;

    public CcResolutionService(
        IEmailValidationService emailValidation,
        IAppLogger<CcResolutionService> logger)
    {
        _emailValidation = emailValidation;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ResolveCc(
        string? staticCcAddresses,
        string? dynamicCcField,
        IDictionary<string, object?> recipientData)
    {
        ArgumentNullException.ThrowIfNull(recipientData);

        var combined = new List<string>();

        // 1. Static CC: comma-separated from campaign configuration (BR-1)
        if (!string.IsNullOrWhiteSpace(staticCcAddresses))
        {
            var staticParsed = staticCcAddresses
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var staticValid = _emailValidation.FilterValid(staticParsed, "StaticCC");
            combined.AddRange(staticValid);
        }

        // 2. Dynamic CC: per-recipient field value (BR-2)
        if (!string.IsNullOrWhiteSpace(dynamicCcField)
            && recipientData.TryGetValue(dynamicCcField, out var dynValue)
            && dynValue is not null)
        {
            var dynStr = dynValue.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(dynStr))
            {
                // Field may contain semicolon or comma-separated addresses
                var dynParsed = dynStr
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var dynValid = _emailValidation.FilterValid(dynParsed, "DynamicCC");
                combined.AddRange(dynValid);
            }
        }

        // 3. Deduplicate — case-insensitive (BR-5)
        var deduplicated = Deduplicate(combined);

        // 4. Enforce max 10 CC recipients (BR-4)
        if (deduplicated.Count > MaxCcRecipientsPerSend)
        {
            _logger.LogWarning(
                "CC recipient count {Count} exceeds maximum {Max} per send. " +
                "Truncating to first {Max} addresses.",
                deduplicated.Count, MaxCcRecipientsPerSend, MaxCcRecipientsPerSend);

            deduplicated = deduplicated.Take(MaxCcRecipientsPerSend).ToList();
        }

        return deduplicated;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ResolveBcc(string? staticBccAddresses)
    {
        if (string.IsNullOrWhiteSpace(staticBccAddresses))
            return [];

        var parsed = staticBccAddresses
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var valid = _emailValidation.FilterValid(parsed, "StaticBCC");

        // Deduplicate BCC as well
        return Deduplicate(valid);
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Deduplicates email addresses using case-insensitive comparison.
    /// Preserves original casing of the first occurrence of each address.
    /// </summary>
    private static List<string> Deduplicate(IEnumerable<string> addresses)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var address in addresses)
        {
            if (seen.Add(address))
                result.Add(address);
        }

        return result;
    }
}
