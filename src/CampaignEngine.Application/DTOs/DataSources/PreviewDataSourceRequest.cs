using CampaignEngine.Domain.Filters;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Request body for the POST /api/datasources/{id}/preview endpoint.
/// Applies an optional filter expression tree to the data source and returns the first 100 rows.
/// </summary>
public class PreviewDataSourceRequest
{
    /// <summary>
    /// The filter expression tree to apply.
    /// Null or empty means no filter — returns the first 100 rows unfiltered.
    ///
    /// Each entry is a top-level filter node; multiple top-level nodes are combined with AND.
    ///
    /// JSON example:
    /// <code>
    /// [
    ///   {
    ///     "type": "leaf",
    ///     "fieldName": "Age",
    ///     "operator": 3,
    ///     "value": 18
    ///   },
    ///   {
    ///     "type": "composite",
    ///     "logicalOperator": 2,
    ///     "children": [
    ///       { "type": "leaf", "fieldName": "Status", "operator": 1, "value": "active" },
    ///       { "type": "leaf", "fieldName": "Status", "operator": 1, "value": "trial" }
    ///     ]
    ///   }
    /// ]
    /// </code>
    /// </summary>
    public IReadOnlyList<FilterExpression>? Filters { get; set; }

    /// <summary>
    /// Optional: maximum number of rows to return.
    /// Business rule: capped at 100 regardless of the requested value.
    /// Defaults to 100.
    /// </summary>
    public int? MaxRows { get; set; }
}
