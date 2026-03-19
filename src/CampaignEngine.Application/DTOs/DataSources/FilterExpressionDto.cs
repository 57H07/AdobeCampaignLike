namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Represents a filter expression node in the AST (Abstract Syntax Tree).
/// Used for parameterized query generation without raw SQL exposure.
/// </summary>
public class FilterExpressionDto
{
    /// <summary>
    /// Logical operator for combining conditions (AND, OR). Null for leaf nodes.
    /// </summary>
    public string? LogicalOperator { get; set; }

    /// <summary>
    /// Field name for leaf condition nodes.
    /// </summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// Comparison operator: =, !=, >, <, >=, <=, LIKE, IN.
    /// </summary>
    public string? Operator { get; set; }

    /// <summary>
    /// Value for leaf condition nodes.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Child expressions for composite nodes.
    /// </summary>
    public IReadOnlyList<FilterExpressionDto>? Children { get; set; }
}
