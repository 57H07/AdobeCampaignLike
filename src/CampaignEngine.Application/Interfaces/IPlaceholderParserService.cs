using CampaignEngine.Application.DTOs.Templates;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service for extracting placeholder keys from template HTML/body content.
/// Understands the Scriban placeholder syntax used by CampaignEngine templates.
/// </summary>
public interface IPlaceholderParserService
{
    /// <summary>
    /// Parses the given template HTML body and extracts all placeholder keys.
    /// Recognizes:
    ///   - Scalar syntax: {{ key }}
    ///   - Iteration block syntax: {{ for item in collectionKey }}
    /// Returns grouped extraction result.
    /// </summary>
    PlaceholderExtractionResult ExtractPlaceholders(string htmlBody);

    /// <summary>
    /// Validates that all placeholder keys found in the template HTML are declared
    /// in the provided manifest entry set. Also identifies orphan manifest entries.
    /// </summary>
    ManifestValidationResult ValidateManifestCompleteness(
        string htmlBody,
        IEnumerable<PlaceholderManifestEntryDto> manifestEntries);
}
