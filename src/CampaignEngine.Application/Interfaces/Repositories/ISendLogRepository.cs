using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for SendLog entries.
/// </summary>
public interface ISendLogRepository : IRepository<SendLog>
{
    /// <summary>
    /// Returns a paged, filtered list of send logs, ordered by CreatedAt descending.
    /// </summary>
    Task<IReadOnlyList<SendLog>> QueryAsync(
        Guid? campaignId,
        string? recipientAddress,
        SendStatus? status,
        DateTime? from,
        DateTime? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of send logs matching the given filters.
    /// </summary>
    Task<int> CountAsync(
        Guid? campaignId,
        string? recipientAddress,
        SendStatus? status,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a tracked SendLog by ID. Returns null if not found.
    /// </summary>
    Task<SendLog?> FindByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a tracked SendLog by ExternalMessageId.
    /// Returns null if not found.
    /// </summary>
    Task<SendLog?> FindByExternalMessageIdAsync(string externalMessageId, CancellationToken cancellationToken = default);
}
