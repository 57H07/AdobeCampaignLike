using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for CampaignAttachment entities.
///
/// US-028: Static and dynamic attachment management.
/// </summary>
public sealed class AttachmentRepository : RepositoryBase<CampaignAttachment>, IAttachmentRepository
{
    public AttachmentRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignAttachment>> GetByCampaignIdAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.CampaignAttachments
            .Where(a => a.CampaignId == campaignId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> GetTotalFileSizeByCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.CampaignAttachments
            .Where(a => a.CampaignId == campaignId && !a.IsDynamic)
            .SumAsync(a => a.FileSizeBytes, cancellationToken);
    }
}
