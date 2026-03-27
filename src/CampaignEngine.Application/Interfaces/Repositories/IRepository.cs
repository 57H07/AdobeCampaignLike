namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Generic repository contract. Provides basic CRUD operations.
/// SaveChanges is intentionally absent — use IUnitOfWork.CommitAsync() instead.
/// </summary>
public interface IRepository<T> where T : class
{
    /// <summary>Finds an entity by its primary key. Returns null if not found.</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new entity to the change tracker (not yet persisted).</summary>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Marks an entity as modified in the change tracker.</summary>
    void Update(T entity);

    /// <summary>Marks an entity for deletion in the change tracker.</summary>
    void Remove(T entity);
}
