using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Validates whether a data source connection string can establish a live connection.
/// Implementations use the appropriate connector strategy per DataSourceType.
/// </summary>
public interface IConnectionTestService
{
    /// <summary>
    /// Tests a plaintext connection string against the given data source type.
    /// No data is written; the connection is opened and immediately closed.
    /// </summary>
    Task<ConnectionTestResult> TestAsync(
        DataSourceType type,
        string plainTextConnectionString,
        CancellationToken cancellationToken = default);
}
