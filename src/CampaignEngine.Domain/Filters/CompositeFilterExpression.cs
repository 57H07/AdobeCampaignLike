using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Filters;

/// <summary>
/// A composite node in the filter AST that groups child expressions using a logical operator.
/// Examples:
///   AND(Age &gt; 18, Status = 'active')
///   OR(Country = 'FR', Country = 'DE')
///   AND(IsActive = true, OR(Role = 'admin', Role = 'manager'))
/// </summary>
public sealed class CompositeFilterExpression : FilterExpression
{
    /// <inheritdoc />
    public override string NodeType => "composite";

    /// <summary>
    /// How child expressions are combined: AND requires all to be true, OR requires at least one.
    /// </summary>
    public LogicalOperator LogicalOperator { get; set; } = LogicalOperator.And;

    /// <summary>
    /// Child expressions. Must contain at least one child.
    /// Maximum nesting depth is 5 levels (enforced by the SQL translator).
    /// </summary>
    public IReadOnlyList<FilterExpression> Children { get; set; } = [];
}
