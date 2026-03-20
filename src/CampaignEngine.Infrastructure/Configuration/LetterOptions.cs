namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the Letter channel dispatcher.
/// Bound from appsettings.json section "Letter".
///
/// TASK-021-02: PDF generation configuration.
/// TASK-021-04: Print provider file drop configuration.
///
/// Business rules (US-021):
///   BR-1: A4 format, portrait orientation (enforced by LetterPostProcessor).
///   BR-2: Consolidation ordered by recipient ID or campaign sequence (natural insertion order).
///   BR-3: Manifest file: CSV with recipient metadata.
///   BR-4: File naming convention: CAMPAIGN_{id}_{timestamp}.pdf
///
/// Open Question Q5: Print provider format — defaults to UNC/local file drop.
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
    /// Output directory for generated PDF batch files.
    /// Supports UNC paths (\\server\share\letters) or local paths (C:\Letters\Output).
    /// Created automatically if it does not exist.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Maximum pages per consolidated batch PDF file.
    /// Business rule BR-4: default 500 pages per batch.
    /// </summary>
    public int MaxPagesPerBatch { get; set; } = 500;

    /// <summary>
    /// Whether to generate a CSV manifest file alongside each PDF batch.
    /// Business rule BR-3: CSV manifest with recipient metadata.
    /// </summary>
    public bool GenerateManifest { get; set; } = true;

    /// <summary>
    /// File name prefix for output files.
    /// Business rule BR-4: naming convention CAMPAIGN_{id}_{timestamp}.
    /// </summary>
    public string FileNamePrefix { get; set; } = "CAMPAIGN";

    /// <summary>
    /// Whether to write individual per-recipient PDFs to the output directory
    /// in addition to the consolidated batch files. Default: false.
    /// </summary>
    public bool WriteIndividualFiles { get; set; } = false;
}
