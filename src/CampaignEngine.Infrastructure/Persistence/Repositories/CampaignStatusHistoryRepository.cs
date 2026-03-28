using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ICampaignStatusHistoryRepository.
/// </summary>
public sealed class CampaignStatusHistoryRepository
    : RepositoryBase<CampaignStatusHistory>, ICampaignStatusHistoryRepository
{
    public CampaignStatusHistoryRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignStatusHistory>> GetByCampaignIdAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var items = await DbContext.CampaignStatusHistories
            .AsNoTracking()
            .Where(h => h.CampaignId == campaignId)
            .OrderBy(h => h.OccurredAt)
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }
}
