namespace CampaignEngine.Domain.Enums;

/// <summary>
/// Logical operator for combining child filter expressions.
/// </summary>
public enum LogicalOperator
{
    /// <summary>All child conditions must be true.</summary>
    And = 1,

    /// <summary>At least one child condition must be true.</summary>
    Or = 2
}
