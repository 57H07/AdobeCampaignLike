using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Resolves the appropriate IDataSourceConnector at runtime based on DataSourceType.
///
/// Registration (in ServiceCollectionExtensions):
///   - SqlServerConnector → DataSourceType.SqlServer
///   - RestApiConnector   → DataSourceType.RestApi
///
/// To add a new connector type:
///   1. Create a class implementing IDataSourceConnector.
///   2. Register it in DI in ServiceCollectionExtensions.
///   3. Add a mapping entry in the constructor here.
/// </summary>
public sealed class DataSourceConnectorRegistry : IDataSourceConnectorRegistry
{
    private readonly IReadOnlyDictionary<DataSourceType, IDataSourceConnector> _connectors;

    public DataSourceConnectorRegistry(
        SqlServerConnector sqlServerConnector,
        RestApiConnector restApiConnector)
    {
        _connectors = new Dictionary<DataSourceType, IDataSourceConnector>
        {
            [DataSourceType.SqlServer] = sqlServerConnector,
            [DataSourceType.RestApi]   = restApiConnector
        };
    }

    /// <inheritdoc />
    public IDataSourceConnector GetConnector(DataSourceType type)
    {
        if (_connectors.TryGetValue(type, out var connector))
            return connector;

        throw new InvalidOperationException(
            $"No data source connector is registered for type '{type}'. " +
            $"Registered types: {string.Join(", ", _connectors.Keys)}.");
    }

    /// <inheritdoc />
    public bool HasConnector(DataSourceType type) => _connectors.ContainsKey(type);
}
