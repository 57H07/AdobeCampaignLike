using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IApiKeyRepository.
/// Encapsulates all LINQ queries that were previously inlined in ApiKeyService.
/// </summary>
public sealed class ApiKeyRepository : RepositoryBase<ApiKey>, IApiKeyRepository
{
    public ApiKeyRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await DbContext.ApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbContext.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default)
        => await DbContext.ApiKeys.AnyAsync(k => k.Name == name, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> GetActiveCandidatesByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        var items = await DbContext.ApiKeys
            .Where(k => k.KeyPrefix == prefix && k.IsActive)
            .ToListAsync(cancellationToken);

        return items.AsReadOnly();
    }
}
