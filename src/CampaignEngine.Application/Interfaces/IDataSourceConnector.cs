using CampaignEngine.Application.DTOs.DataSources;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Abstraction for data source connectors (SQL Server, REST API, etc.).
/// Registered in DI container via strategy pattern keyed by DataSourceType.
/// </summary>
public interface IDataSourceConnector
{
    /// <summary>
    /// Queries the data source applying provided filters and returns rows as dictionaries.
    /// </summary>
    Task<IReadOnlyList<IDictionary<string, object?>>> QueryAsync(
        DataSourceDefinitionDto definition,
        IReadOnlyList<FilterExpressionDto>? filters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the schema (field names, types) from the data source.
    /// </summary>
    Task<IReadOnlyList<FieldDefinitionDto>> GetSchemaAsync(
        DataSourceDefinitionDto definition,
        CancellationToken cancellationToken = default);
}
