namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Abstraction over the EF Core DbContext transaction boundary.
/// Services call CommitAsync() instead of SaveChangesAsync() directly,
/// keeping persistence concerns out of the Application layer.
/// IAsyncDisposable: auto-rollback any uncommitted transaction on dispose.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Flushes all pending changes to the database.
    /// Equivalent to DbContext.SaveChangesAsync().
    /// </summary>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins an explicit database transaction.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves changes and commits the current transaction.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current transaction without saving.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a raw SQL command against the database.
    /// Used for atomic operations (e.g., incrementing counters) that EF cannot express safely.
    /// </summary>
    Task<int> ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken = default, params object[] parameters);
}
