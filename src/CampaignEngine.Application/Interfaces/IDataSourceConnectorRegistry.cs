using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Registry that resolves the appropriate IDataSourceConnector for a given DataSourceType.
/// Uses DI-based strategy pattern — no hardcoded switch/case.
/// New connector types can be added by registering a new implementation and updating the registry.
/// </summary>
public interface IDataSourceConnectorRegistry
{
    /// <summary>
    /// Returns the connector registered for the specified data source type.
    /// </summary>
    /// <param name="type">The data source type (SqlServer, RestApi, etc.).</param>
    /// <returns>The connector for that type.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no connector is registered for the requested type.
    /// </exception>
    IDataSourceConnector GetConnector(DataSourceType type);

    /// <summary>
    /// Returns true if a connector is registered for the specified type.
    /// </summary>
    bool HasConnector(DataSourceType type);
}
