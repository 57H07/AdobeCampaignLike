using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for ApiKey entities.
/// </summary>
public interface IApiKeyRepository : IRepository<ApiKey>
{
    /// <summary>
    /// Returns all API keys ordered by CreatedAt descending. AsNoTracking.
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single API key by ID. AsNoTracking.
    /// Returns null if not found.
    /// </summary>
    Task<ApiKey?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns true if an API key with the given name exists.</summary>
    Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns active API keys whose KeyPrefix matches (for BCrypt validation).
    /// Tracked entities (LastUsedAt is updated after successful validation).
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetActiveCandidatesByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default);
}
