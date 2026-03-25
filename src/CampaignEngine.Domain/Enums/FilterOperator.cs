namespace CampaignEngine.Domain.Enums;

/// <summary>
/// Supported comparison operators for filter expressions.
/// All operators translate to parameterized SQL — no raw user values are interpolated into SQL text.
/// </summary>
public enum FilterOperator
{
    /// <summary>Equality check: field = value</summary>
    Equals = 1,

    /// <summary>Inequality check: field &lt;&gt; value</summary>
    NotEquals = 2,

    /// <summary>Greater-than comparison: field &gt; value</summary>
    GreaterThan = 3,

    /// <summary>Less-than comparison: field &lt; value</summary>
    LessThan = 4,

    /// <summary>Greater-than-or-equal comparison: field &gt;= value</summary>
    GreaterThanOrEquals = 5,

    /// <summary>Less-than-or-equal comparison: field &lt;= value</summary>
    LessThanOrEquals = 6,

    /// <summary>Pattern match: field LIKE '%value%'. Wildcards (%, _) may be included in value.</summary>
    Like = 7,

    /// <summary>Set membership: field IN (value1, value2, ...). Supports up to 1000 values.</summary>
    In = 8,

    /// <summary>Null check: field IS NULL. No value required.</summary>
    IsNull = 9,

    /// <summary>Not-null check: field IS NOT NULL. No value required.</summary>
    IsNotNull = 10
}
