using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Domain.Filters;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Translates a filter AST (Abstract Syntax Tree) into a parameterized SQL WHERE clause.
///
/// Security contract:
///   - All filter VALUES are returned as named parameters in <see cref="SqlTranslationResult.Parameters"/>.
///   - Column identifiers are validated against the known schema and bracket-quoted.
///   - Operators are whitelisted — no arbitrary SQL is accepted.
///   - The resulting WHERE clause is safe to append to a parameterized Dapper query.
///
/// Usage:
/// <code>
///   var result = translator.Translate(filters, knownFields);
///   // result.WhereClause: "([Age] > @p0 AND [Status] = @p1)"
///   // result.Parameters:  { "@p0" = 18, "@p1" = "active" }
/// </code>
/// </summary>
public interface IFilterAstTranslator
{
    /// <summary>
    /// Translates a list of top-level filter expressions to a SQL WHERE clause string and parameter map.
    /// Top-level expressions are implicitly combined with AND.
    /// Returns null WhereClause when the filter list is empty.
    /// </summary>
    /// <param name="filters">The filter expressions to translate.</param>
    /// <param name="knownFields">Declared schema fields used to validate field names.</param>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when a field name is not in the known schema, an operator is not whitelisted,
    ///   or the nesting depth exceeds the maximum.
    /// </exception>
    SqlTranslationResult Translate(
        IReadOnlyList<FilterExpression> filters,
        IReadOnlyList<FieldDefinitionDto> knownFields);

    /// <summary>
    /// Translates a single filter expression tree to a SQL WHERE clause and parameter map.
    /// </summary>
    SqlTranslationResult TranslateSingle(
        FilterExpression filter,
        IReadOnlyList<FieldDefinitionDto> knownFields);
}

/// <summary>
/// The output of an AST-to-SQL translation: the WHERE clause fragment and named SQL parameters.
/// </summary>
public sealed class SqlTranslationResult
{
    /// <summary>
    /// The SQL WHERE clause fragment (without the "WHERE" keyword).
    /// Null when there are no filters. Example: "([Age] &gt; @p0 AND [Status] = @p1)"
    /// </summary>
    public string? WhereClause { get; init; }

    /// <summary>
    /// Named SQL parameters corresponding to placeholder references in <see cref="WhereClause"/>.
    /// Keys are parameter names (e.g., "@p0"); values are the typed filter values.
    /// For IN operator, each list item gets its own parameter (e.g., "@p0_0", "@p0_1").
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
}
