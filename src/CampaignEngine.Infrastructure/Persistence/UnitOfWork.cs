using CampaignEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CampaignEngine.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IUnitOfWork.
/// Wraps CampaignEngineDbContext to control transaction boundaries.
/// Auto-rollbacks any uncommitted transaction on dispose.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly CampaignEngineDbContext _dbContext;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(CampaignEngineDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        => _currentTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

    /// <inheritdoc />
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _currentTransaction!.CommitAsync(cancellationToken);
        _currentTransaction = null;
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        await _currentTransaction!.RollbackAsync(cancellationToken);
        _currentTransaction = null;
    }

    /// <inheritdoc />
    public Task<int> ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken = default, params object[] parameters)
        => _dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction is not null)
        {
            await _currentTransaction.RollbackAsync();
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }
}
