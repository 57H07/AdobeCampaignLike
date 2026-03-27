using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Filters;

/// <summary>
/// Root class for the filter Abstract Syntax Tree (AST).
/// A FilterExpression is either:
///   - A <see cref="CompositeFilterExpression"/> (AND/OR group of child expressions), or
///   - A <see cref="LeafFilterExpression"/> (a single field-operator-value condition).
///
/// The tree is serialized to JSON for storage and deserialized for SQL translation.
/// All values are parameterized when converted to SQL — no raw value interpolation.
/// </summary>
public abstract class FilterExpression
{
    /// <summary>
    /// Discriminator used to distinguish composite vs. leaf nodes ("composite" or "leaf").
    /// </summary>
    public abstract string NodeType { get; }

    // ----------------------------------------------------------------
    // Factory helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates a leaf condition node for a single field comparison.
    /// </summary>
    public static LeafFilterExpression Leaf(string fieldName, FilterOperator op, object? value = null)
        => new() { FieldName = fieldName, Operator = op, Value = value };

    /// <summary>
    /// Creates a composite AND node grouping multiple child expressions.
    /// </summary>
    public static CompositeFilterExpression And(params FilterExpression[] children)
        => new() { LogicalOperator = LogicalOperator.And, Children = children.ToList() };

    /// <summary>
    /// Creates a composite OR node grouping multiple child expressions.
    /// </summary>
    public static CompositeFilterExpression Or(params FilterExpression[] children)
        => new() { LogicalOperator = LogicalOperator.Or, Children = children.ToList() };
}
