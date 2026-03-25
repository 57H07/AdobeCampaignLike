using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Splits a flat list of recipient rows into fixed-size chunks
/// for parallel Hangfire job processing.
///
/// Algorithm: simple sequential partitioning — recipients[0..chunkSize-1],
/// recipients[chunkSize..2*chunkSize-1], etc.
/// Each chunk knows its own index and the total chunk count so that the
/// Chunk Coordinator can detect completion atomically.
/// </summary>
public sealed class RecipientChunkingService : IRecipientChunkingService
{
    private const int MinChunkSize = 1;
    private const int MaxChunkSize = 10_000;

    private readonly BatchProcessingOptions _options;

    public RecipientChunkingService(IOptions<CampaignEngineOptions> options)
    {
        _options = options.Value.BatchProcessing;
    }

    /// <inheritdoc />
    public IReadOnlyList<RecipientChunk> Split(
        IReadOnlyList<IDictionary<string, object?>> recipients,
        int? chunkSize = null)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        var size = chunkSize ?? _options.ChunkSize;
        size = Math.Clamp(size, MinChunkSize, MaxChunkSize);

        if (recipients.Count == 0)
        {
            // Return a single empty chunk so the coordinator always has at least one job
            return [new RecipientChunk(0, 1, [])];
        }

        var chunks = new List<RecipientChunk>();
        var totalChunks = (int)Math.Ceiling((double)recipients.Count / size);

        for (var i = 0; i < recipients.Count; i += size)
        {
            var slice = recipients
                .Skip(i)
                .Take(size)
                .ToList();

            chunks.Add(new RecipientChunk(
                ChunkIndex: i / size,
                TotalChunks: totalChunks,
                Recipients: slice));
        }

        return chunks;
    }
}
