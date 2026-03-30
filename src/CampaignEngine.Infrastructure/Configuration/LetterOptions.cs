namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the Letter channel dispatcher.
/// Bound from appsettings.json section "Letter".
///
/// US-023: Rewritten for DOCX file drop (one file per recipient, no PDF consolidation).
///
/// Business rules (US-023):
///   BR-1: One SendAsync call = one DOCX file written.
///   BR-2: No batch accumulation or consolidation.
///   BR-3: File naming: {campaignId}_{recipientId}_{timestamp}.docx
/// </summary>
public class LetterOptions
{
    public const string SectionName = "Letter";

    /// <summary>
    /// Whether the Letter channel is enabled for dispatch.
    /// When false, all send attempts return a permanent failure.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Output directory for generated DOCX files.
    /// Supports UNC paths (\\server\share\letters) or local paths (C:\Letters\Output).
    /// Created automatically if it does not exist.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;
}
