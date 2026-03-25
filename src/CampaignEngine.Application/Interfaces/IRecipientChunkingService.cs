namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Splits a flat list of recipient rows into fixed-size chunks
/// for parallel Hangfire job processing.
/// </summary>
public interface IRecipientChunkingService
{
    /// <summary>
    /// Splits recipient rows into chunks of the configured size (default 500).
    /// Each chunk contains a subset of serialized recipient data.
    /// </summary>
    /// <param name="recipients">All recipient rows for the campaign step.</param>
    /// <param name="chunkSize">
    ///   Number of recipients per chunk. Defaults to BatchProcessingOptions.ChunkSize (500).
    ///   Must be between 1 and 10000.
    /// </param>
    /// <returns>Ordered list of recipient chunks; never empty.</returns>
    IReadOnlyList<RecipientChunk> Split(
        IReadOnlyList<IDictionary<string, object?>> recipients,
        int? chunkSize = null);
}

/// <summary>
/// A single batch of recipients ready for enqueuing as a Hangfire job.
/// </summary>
public record RecipientChunk(
    int ChunkIndex,
    int TotalChunks,
    IReadOnlyList<IDictionary<string, object?>> Recipients);
