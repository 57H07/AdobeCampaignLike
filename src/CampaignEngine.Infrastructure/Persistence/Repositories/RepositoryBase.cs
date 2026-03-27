using CampaignEngine.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// Base EF Core repository providing standard CRUD operations.
/// Derived repositories add entity-specific query methods.
/// </summary>
public abstract class RepositoryBase<T> : IRepository<T> where T : class
{
    protected readonly CampaignEngineDbContext DbContext;

    protected RepositoryBase(CampaignEngineDbContext dbContext) => DbContext = dbContext;

    /// <inheritdoc />
    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.Set<T>().FindAsync([id], cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await DbContext.Set<T>().AddAsync(entity, cancellationToken);

    /// <inheritdoc />
    public void Update(T entity) => DbContext.Set<T>().Update(entity);

    /// <inheritdoc />
    public void Remove(T entity) => DbContext.Set<T>().Remove(entity);
}
