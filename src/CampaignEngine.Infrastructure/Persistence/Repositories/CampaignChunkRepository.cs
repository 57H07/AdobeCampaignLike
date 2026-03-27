using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ICampaignChunkRepository.
/// Encapsulates chunk-related queries for the Chunk Coordinator pattern.
/// </summary>
public sealed class CampaignChunkRepository : RepositoryBase<CampaignChunk>, ICampaignChunkRepository
{
    public CampaignChunkRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<CampaignChunk?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.CampaignChunks
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<int> CountPendingOrProcessingAsync(
        Guid campaignStepId,
        CancellationToken cancellationToken = default)
        => await DbContext.CampaignChunks
            .CountAsync(c =>
                c.CampaignStepId == campaignStepId &&
                (c.Status == ChunkStatus.Pending || c.Status == ChunkStatus.Processing),
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkStatusCount>> GetStatusCountsAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var results = await DbContext.CampaignChunks
            .AsNoTracking()
            .Where(c => c.CampaignId == campaignId)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return results
            .Select(r => new ChunkStatusCount(r.Status, r.Count))
            .ToList()
            .AsReadOnly();
    }
}
