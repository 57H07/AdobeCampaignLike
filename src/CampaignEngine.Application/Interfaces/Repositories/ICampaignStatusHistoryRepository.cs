using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for CampaignStatusHistory records.
/// </summary>
public interface ICampaignStatusHistoryRepository : IRepository<CampaignStatusHistory>
{
    /// <summary>
    /// Returns all history entries for a campaign, ordered by OccurredAt ascending. AsNoTracking.
    /// </summary>
    Task<IReadOnlyList<CampaignStatusHistory>> GetByCampaignIdAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
