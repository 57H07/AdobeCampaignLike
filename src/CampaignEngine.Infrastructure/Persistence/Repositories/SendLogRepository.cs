using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ISendLogRepository.
/// Encapsulates all LINQ queries that were previously inlined in SendLogService.
/// </summary>
public sealed class SendLogRepository : RepositoryBase<SendLog>, ISendLogRepository
{
    public SendLogRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SendLog>> QueryAsync(
        Guid? campaignId,
        string? recipientAddress,
        SendStatus? status,
        DateTime? from,
        DateTime? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(campaignId, recipientAddress, status, from, to);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        Guid? campaignId,
        string? recipientAddress,
        SendStatus? status,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(campaignId, recipientAddress, status, from, to);
        return await query.CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SendLog?> FindByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.SendLogs.FindAsync([id], cancellationToken);

    /// <inheritdoc />
    public async Task<SendLog?> FindByExternalMessageIdAsync(
        string externalMessageId,
        CancellationToken cancellationToken = default)
        => await DbContext.SendLogs
            .FirstOrDefaultAsync(l => l.ExternalMessageId == externalMessageId, cancellationToken);

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private IQueryable<SendLog> BuildQuery(
        Guid? campaignId,
        string? recipientAddress,
        SendStatus? status,
        DateTime? from,
        DateTime? to)
    {
        var query = DbContext.SendLogs.AsQueryable();

        if (campaignId.HasValue)
            query = query.Where(l => l.CampaignId == campaignId.Value);

        if (!string.IsNullOrWhiteSpace(recipientAddress))
            query = query.Where(l => l.RecipientAddress.Contains(recipientAddress));

        if (status.HasValue)
            query = query.Where(l => l.Status == status.Value);

        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.CreatedAt <= to.Value);

        return query;
    }
}
