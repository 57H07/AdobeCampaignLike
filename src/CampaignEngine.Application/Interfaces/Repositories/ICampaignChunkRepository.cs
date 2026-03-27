using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for CampaignChunk entities used by the chunk coordinator.
/// </summary>
public interface ICampaignChunkRepository : IRepository<CampaignChunk>
{
    /// <summary>
    /// Returns a tracked chunk by ID. Returns null if not found.
    /// </summary>
    Task<CampaignChunk?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of chunks for a given step that are still Pending or Processing.
    /// Used for completion detection.
    /// </summary>
    Task<int> CountPendingOrProcessingAsync(Guid campaignStepId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns chunk statistics grouped by Status for a given campaign.
    /// </summary>
    Task<IReadOnlyList<ChunkStatusCount>> GetStatusCountsAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}

/// <summary>Projection for chunk status aggregation.</summary>
public sealed record ChunkStatusCount(ChunkStatus Status, int Count);
