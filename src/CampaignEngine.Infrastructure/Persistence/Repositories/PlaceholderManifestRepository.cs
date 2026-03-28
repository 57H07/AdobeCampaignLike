using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IPlaceholderManifestRepository.
/// </summary>
public sealed class PlaceholderManifestRepository
    : RepositoryBase<PlaceholderManifestEntry>, IPlaceholderManifestRepository
{
    public PlaceholderManifestRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaceholderManifestEntry>> GetByTemplateIdAsync(
        Guid templateId,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = DbContext.PlaceholderManifests
            .Where(p => p.TemplateId == templateId);

        if (noTracking)
            baseQuery = baseQuery.AsNoTracking();

        var items = await baseQuery.OrderBy(p => p.Key).ToListAsync(cancellationToken);
        return items.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<PlaceholderManifestEntry?> GetByIdAndTemplateIdAsync(
        Guid id,
        Guid templateId,
        CancellationToken cancellationToken = default)
        => await DbContext.PlaceholderManifests
            .FirstOrDefaultAsync(p => p.Id == id && p.TemplateId == templateId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> KeyExistsAsync(
        Guid templateId,
        string key,
        Guid? excludeEntryId = null,
        CancellationToken cancellationToken = default)
    {
        var query = DbContext.PlaceholderManifests
            .Where(p => p.TemplateId == templateId && p.Key == key);

        if (excludeEntryId.HasValue)
            query = query.Where(p => p.Id != excludeEntryId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void RemoveRange(IEnumerable<PlaceholderManifestEntry> entries)
        => DbContext.PlaceholderManifests.RemoveRange(entries);

    /// <inheritdoc />
    public async Task AddRangeAsync(
        IEnumerable<PlaceholderManifestEntry> entries,
        CancellationToken cancellationToken = default)
        => await DbContext.PlaceholderManifests.AddRangeAsync(entries, cancellationToken);
}
