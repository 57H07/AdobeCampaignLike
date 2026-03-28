using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for CampaignAttachment entities.
///
/// US-028: Static and dynamic attachment management.
/// </summary>
public interface IAttachmentRepository : IRepository<CampaignAttachment>
{
    /// <summary>
    /// Returns all attachments belonging to a campaign, ordered by creation date.
    /// </summary>
    Task<IReadOnlyList<CampaignAttachment>> GetByCampaignIdAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total size in bytes of all static attachments for a campaign.
    /// Used to enforce the 25 MB per-send total size limit.
    /// </summary>
    Task<long> GetTotalFileSizeByCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
