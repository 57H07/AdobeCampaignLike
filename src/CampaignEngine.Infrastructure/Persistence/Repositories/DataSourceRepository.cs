using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IDataSourceRepository.
/// Encapsulates all LINQ queries that were previously inlined in DataSourceService.
/// </summary>
public sealed class DataSourceRepository : RepositoryBase<DataSource>, IDataSourceRepository
{
    public DataSourceRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<DataSource> Items, int Total)> GetPagedAsync(
        DataSourceFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = DbContext.DataSources
            .Include(d => d.Fields)
            .AsQueryable();

        if (filter.Type.HasValue)
            query = query.Where(d => d.Type == filter.Type.Value);

        if (filter.IsActive.HasValue)
            query = query.Where(d => d.IsActive == filter.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
            query = query.Where(d => d.Name.Contains(filter.NameContains));

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderBy(d => d.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items.AsReadOnly(), total);
    }

    /// <inheritdoc />
    public async Task<DataSource?> GetWithFieldsAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<DataSource?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.DataSources
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default)
        => await DbContext.DataSources.AnyAsync(d => d.Name == name, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ExistsWithNameExcludingAsync(
        string name,
        Guid excludeId,
        CancellationToken cancellationToken = default)
        => await DbContext.DataSources.AnyAsync(
            d => d.Name == name && d.Id != excludeId,
            cancellationToken);

    /// <inheritdoc />
    public void RemoveFieldRange(IEnumerable<DataSourceField> fields)
        => DbContext.DataSourceFields.RemoveRange(fields);

    /// <inheritdoc />
    public void AddFieldRange(IEnumerable<DataSourceField> fields)
        => DbContext.DataSourceFields.AddRange(fields);

    /// <inheritdoc />
    public async Task ReloadFieldsAsync(DataSource dataSource, CancellationToken cancellationToken = default)
        => await DbContext.Entry(dataSource)
            .Collection(d => d.Fields)
            .LoadAsync(cancellationToken);
}
