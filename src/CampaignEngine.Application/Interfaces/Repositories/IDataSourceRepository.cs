using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for DataSource aggregate (includes Fields navigation).
/// </summary>
public interface IDataSourceRepository : IRepository<DataSource>
{
    /// <summary>
    /// Returns a paginated filtered list of data sources with their Fields.
    /// </summary>
    Task<(IReadOnlyList<DataSource> Items, int Total)> GetPagedAsync(
        DataSourceFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a data source with its Fields navigation. Returns null if not found.
    /// </summary>
    Task<DataSource?> GetWithFieldsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a lightweight data source (no Fields). Returns null if not found.
    /// </summary>
    Task<DataSource?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a data source with the given name exists.</summary>
    Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a data source with the given name exists, excluding a specific ID (for self-update).</summary>
    Task<bool> ExistsWithNameExcludingAsync(string name, Guid excludeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a range of DataSourceField entities from the change tracker.
    /// </summary>
    void RemoveFieldRange(IEnumerable<DataSourceField> fields);

    /// <summary>
    /// Adds a range of DataSourceField entities to the change tracker.
    /// </summary>
    void AddFieldRange(IEnumerable<DataSourceField> fields);

    /// <summary>
    /// Reloads the Fields collection navigation for the given data source
    /// (used after schema update to refresh the collection in-memory).
    /// </summary>
    Task ReloadFieldsAsync(DataSource dataSource, CancellationToken cancellationToken = default);
}
