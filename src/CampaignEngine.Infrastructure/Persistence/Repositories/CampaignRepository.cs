using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ICampaignRepository.
/// Encapsulates all LINQ queries that were previously inlined in CampaignService.
/// </summary>
public sealed class CampaignRepository : RepositoryBase<Campaign>, ICampaignRepository
{
    public CampaignRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<CampaignPagedResult> GetPagedAsync(
        CampaignFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = DbContext.Campaigns
            .Include(c => c.Steps)
            .Include(c => c.DataSource)
            .AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(c => c.Status == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
            query = query.Where(c => c.Name.Contains(filter.NameContains));

        if (filter.DataSourceId.HasValue)
            query = query.Where(c => c.DataSourceId == filter.DataSourceId.Value);

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CampaignPagedResult
        {
            Items = items.Select(c => c.Adapt<CampaignDto>()).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<Campaign?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Campaigns
            .Include(c => c.Steps)
            .Include(c => c.DataSource)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Campaign?> GetWithStepsAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Campaigns
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default)
        => await DbContext.Campaigns.AnyAsync(c => c.Name == name, cancellationToken);

    /// <inheritdoc />
    public async Task<Campaign?> GetNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateValidationProjection>> GetTemplateValidationsAsync(
        IReadOnlyList<Guid> templateIds,
        CancellationToken cancellationToken = default)
    {
        var results = await DbContext.Templates
            .Where(t => templateIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Status, t.Channel })
            .ToListAsync(cancellationToken);

        return results
            .Select(t => new TemplateValidationProjection(t.Id, t.Name, t.Status, t.Channel))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> DataSourceExistsAsync(Guid dataSourceId, CancellationToken cancellationToken = default)
        => await DbContext.DataSources.AnyAsync(d => d.Id == dataSourceId, cancellationToken);
}
