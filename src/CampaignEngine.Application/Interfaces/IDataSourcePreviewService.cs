using CampaignEngine.Application.DTOs.DataSources;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Executes a filtered preview query against a data source.
///
/// Business rules:
///   - Preview is limited to the first 100 rows (hard cap, regardless of request).
///   - Filter expressions are validated before SQL generation.
///   - Only read-only SELECT queries are issued — no DML or DDL.
///   - Connection strings are decrypted at runtime; never exposed to callers.
/// </summary>
public interface IDataSourcePreviewService
{
    /// <summary>
    /// Executes a preview query against the specified data source, applying the provided filter expressions.
    /// Returns up to 100 rows and the total matching row count.
    /// </summary>
    /// <param name="dataSourceId">The data source to query.</param>
    /// <param name="request">The filter expressions and optional row limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CampaignEngine.Domain.Exceptions.NotFoundException">
    ///   Thrown when the data source ID does not exist.
    /// </exception>
    Task<PreviewDataSourceResult> PreviewAsync(
        Guid dataSourceId,
        PreviewDataSourceRequest request,
        CancellationToken cancellationToken = default);
}
