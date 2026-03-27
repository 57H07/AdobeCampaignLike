using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ITemplateRepository.
/// Encapsulates all LINQ queries that were previously inlined in TemplateService
/// and TemplateSnapshotService.
/// </summary>
public sealed class TemplateRepository : RepositoryBase<Template>, ITemplateRepository
{
    public TemplateRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Template> Items, int Total)> GetPagedAsync(
        ChannelType? channel,
        TemplateStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Global query filter on Template already excludes soft-deleted records.
        var query = DbContext.Templates.AsNoTracking();

        if (channel.HasValue)
            query = query.Where(t => t.Channel == channel.Value);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.Channel)
            .ThenBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items.AsReadOnly(), total);
    }

    /// <inheritdoc />
    public async Task<Template?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Template?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Template>> GetSubTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var items = await DbContext.Templates
            .AsNoTracking()
            .Where(t => t.IsSubTemplate)
            .OrderBy(t => t.Channel)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> NameExistsAsync(
        string name,
        ChannelType channel,
        Guid? excludeId,
        CancellationToken cancellationToken = default)
    {
        var query = DbContext.Templates
            .Where(t => t.Name == name && t.Channel == channel);

        if (excludeId.HasValue)
            query = query.Where(t => t.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Templates
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Template?> GetIgnoreQueryFiltersAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Templates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Template>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var items = await DbContext.Templates
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }

    // ----------------------------------------------------------------
    // TemplateHistory
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task AddHistoryAsync(TemplateHistory history, CancellationToken cancellationToken = default)
        => await DbContext.TemplateHistories.AddAsync(history, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateHistory>> GetHistoryAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var items = await DbContext.TemplateHistories
            .AsNoTracking()
            .Where(h => h.TemplateId == templateId)
            .OrderByDescending(h => h.Version)
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<TemplateHistory?> GetHistoryEntryAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken = default)
        => await DbContext.TemplateHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                h => h.TemplateId == templateId && h.Version == version,
                cancellationToken);

    // ----------------------------------------------------------------
    // TemplateSnapshot
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task AddSnapshotAsync(TemplateSnapshot snapshot, CancellationToken cancellationToken = default)
        => await DbContext.TemplateSnapshots.AddAsync(snapshot, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignStep>> GetCampaignStepsWithSnapshotsAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var items = await DbContext.CampaignSteps
            .Where(s => s.CampaignId == campaignId && s.TemplateSnapshotId != null)
            .Include(s => s.TemplateSnapshot)
            .OrderBy(s => s.StepOrder)
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }
}
