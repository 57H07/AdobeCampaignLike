namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Metadata for a single recipient in a letter batch.
/// Used to build the CSV manifest file.
///
/// TASK-021-05: PDF metadata generation (manifest file).
///
/// Business rule BR-3: Manifest file: CSV with recipient metadata.
/// </summary>
public sealed record LetterManifestEntry(
    /// <summary>The recipient's external reference identifier (from data source).</summary>
    string RecipientId,

    /// <summary>The recipient's display name.</summary>
    string? DisplayName,

    /// <summary>Sequence position within the batch (1-based).</summary>
    int SequenceInBatch,

    /// <summary>Number of pages this recipient's letter occupies.</summary>
    int PageCount,

    /// <summary>Name of the batch PDF file this recipient belongs to.</summary>
    string BatchFileName
);
