using CampaignEngine.Application.DTOs.DataSources;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Application service for managing data source declarations.
/// Handles CRUD operations, schema management, and connection testing.
/// Only Admin role can create or edit data sources (Business Rule BR-3).
/// </summary>
public interface IDataSourceService
{
    /// <summary>
    /// Creates a new data source with encrypted connection string.
    /// </summary>
    Task<DataSourceDto> CreateAsync(CreateDataSourceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing data source. Connection string encrypted if changed.
    /// </summary>
    Task<DataSourceDto> UpdateAsync(Guid id, UpdateDataSourceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated, filtered list of data sources.
    /// </summary>
    Task<DataSourcePagedResult> GetAllAsync(DataSourceFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single data source by ID.
    /// </summary>
    Task<DataSourceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity for the specified data source.
    /// Returns a connection test result without persisting anything.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity using the provided (unencrypted) connection string without persisting.
    /// Used during data source creation before saving.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionRawAsync(TestConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or replaces the field schema for a data source.
    /// </summary>
    Task<DataSourceDto> UpdateSchemaAsync(Guid id, IReadOnlyList<UpsertFieldRequest> fields, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates or deactivates a data source.
    /// </summary>
    Task<DataSourceDto> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}
