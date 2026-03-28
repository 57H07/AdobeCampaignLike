using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for Campaign aggregate (includes Steps, DataSource navigation).
/// </summary>
public interface ICampaignRepository : IRepository<Campaign>
{
    /// <summary>
    /// Returns a paginated, filtered list of campaigns including Steps and DataSource.
    /// </summary>
    Task<CampaignPagedResult> GetPagedAsync(CampaignFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a campaign with Steps and DataSource navigation properties.
    /// Returns null if not found.
    /// </summary>
    Task<Campaign?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a campaign with Steps (no DataSource). Used when only steps are needed.
    /// Returns null if not found.
    /// </summary>
    Task<Campaign?> GetWithStepsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a campaign with the given name already exists.</summary>
    Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a lightweight campaign for progress queries (no navigation loads).
    /// Uses AsNoTracking.
    /// </summary>
    Task<Campaign?> GetNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that all templateIds exist in the Templates table and returns
    /// their Id, Name, Status, Channel projections.
    /// </summary>
    Task<IReadOnlyList<TemplateValidationProjection>> GetTemplateValidationsAsync(
        IReadOnlyList<Guid> templateIds,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true if a DataSource with the given ID exists.</summary>
    Task<bool> DataSourceExistsAsync(Guid dataSourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads the entity from the database, discarding any in-memory changes.
    /// Used by CampaignCompletionService to re-read atomic SQL counters.
    /// </summary>
    Task ReloadAsync(Campaign campaign, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a campaign with Steps, DataSource, and DataSource.Fields navigation properties.
    /// Used by ChunkCoordinatorService to query recipients.
    /// Returns null if not found.
    /// </summary>
    Task<Campaign?> GetWithDataSourceFieldsAndStepsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns campaigns matching the given statuses with their Steps loaded.
    /// Used by the dashboard service (US-036).
    /// Results ordered by StartedAt descending, then CreatedAt descending.
    /// </summary>
    Task<IReadOnlyList<Campaign>> GetActiveForDashboardAsync(
        IReadOnlyList<CampaignEngine.Domain.Enums.CampaignStatus> statuses,
        DateTime? startedFrom,
        DateTime? startedTo,
        string? createdBy,
        CancellationToken cancellationToken = default);
}

/// <summary>Lightweight projection used only for validation queries in CampaignService.</summary>
public sealed record TemplateValidationProjection(Guid Id, string Name, TemplateStatus Status, ChannelType Channel);
