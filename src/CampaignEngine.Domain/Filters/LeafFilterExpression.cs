using System.Text.Json.Serialization;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Filters;

/// <summary>
/// A leaf node in the filter AST representing a single field comparison condition.
/// Examples:
///   Age &gt; 18
///   Email LIKE '%@example.com'
///   Status IN ('active', 'trial')
///   DeletedAt IS NULL
/// </summary>
public sealed class LeafFilterExpression : FilterExpression
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public override string NodeType => "leaf";

    /// <summary>
    /// The data source field name to filter on.
    /// Must match a declared field in the data source schema (validated before SQL generation).
    /// </summary>
    [JsonPropertyName("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The comparison operator to apply.
    /// </summary>
    [JsonPropertyName("operator")]
    public FilterOperator Operator { get; set; }

    /// <summary>
    /// The value to compare against.
    ///   - Scalar types (string, int, decimal, DateTime) for most operators.
    ///   - IEnumerable of scalars for the <see cref="FilterOperator.In"/> operator (up to 1000 values).
    ///   - Null for <see cref="FilterOperator.IsNull"/> and <see cref="FilterOperator.IsNotNull"/>.
    ///   - Relative date string for date fields (e.g., "last30days", "last7days", "today").
    ///
    /// All values are passed as Dapper parameters — never interpolated into SQL text.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}
