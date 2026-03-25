namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Result of a data source preview operation.
/// Contains the first N rows (up to 100) and the total row count matching the filters.
/// </summary>
public class PreviewDataSourceResult
{
    /// <summary>
    /// Preview rows — up to 100 rows from the data source matching the applied filters.
    /// Each row is a dictionary of field name to field value.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>
    /// Number of rows in this preview result (may be less than TotalCount).
    /// </summary>
    public int RowCount => Rows.Count;

    /// <summary>
    /// Estimated total rows matching the filter (before the 100-row preview cap).
    /// Null when counting is not supported by the connector (e.g., REST API connectors).
    /// </summary>
    public int? TotalCount { get; init; }

    /// <summary>
    /// The data source ID this preview was fetched from.
    /// </summary>
    public Guid DataSourceId { get; init; }

    /// <summary>
    /// Whether any filters were applied to produce this result.
    /// </summary>
    public bool HasFilters { get; init; }

    /// <summary>
    /// The SQL WHERE clause that was generated from the filter AST (for debugging/transparency).
    /// Only populated when the connector supports SQL translation.
    /// </summary>
    public string? AppliedWhereClause { get; init; }

    /// <summary>
    /// Validation errors found in the filter expression, if any.
    /// When non-empty, the preview was not executed and Rows will be empty.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}
