using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Filters;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Translates a filter AST (Abstract Syntax Tree) into a parameterized SQL WHERE clause.
///
/// Security principles:
///   - All filter VALUES are returned as named parameters — never interpolated into SQL text.
///   - Column identifiers are validated against the declared schema and bracket-quoted.
///   - SQL operators are whitelisted via the <see cref="FilterOperator"/> enum.
///   - No raw SQL strings from user input are ever accepted.
///   - Maximum nesting depth: 5 levels (prevents stack overflow from deeply nested expressions).
///
/// Business rules implemented:
///   - IN operator: supports up to 1000 values (enforced here, validated upstream too).
///   - Date fields: relative filter strings ("last30days", "last7days", "today", etc.)
///     are resolved to concrete DateTime values before parameterization.
/// </summary>
public sealed class FilterAstTranslator : IFilterAstTranslator
{
    private const int MaxNestingDepth = 5;
    private const int MaxInValues = 1000;

    // ----------------------------------------------------------------
    // IFilterAstTranslator.Translate
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public SqlTranslationResult Translate(
        IReadOnlyList<FilterExpression> filters,
        IReadOnlyList<FieldDefinitionDto> knownFields)
    {
        if (filters is null || filters.Count == 0)
            return new SqlTranslationResult { WhereClause = null, Parameters = new Dictionary<string, object?>() };

        var knownFieldNames = BuildFieldNameSet(knownFields);
        var fieldTypeMap = BuildFieldTypeMap(knownFields);
        var parameters = new Dictionary<string, object?>();
        var paramIndex = 0;

        var clauses = new List<string>();
        foreach (var filter in filters)
        {
            var clause = TranslateNode(filter, parameters, knownFieldNames, fieldTypeMap, ref paramIndex, depth: 0);
            if (!string.IsNullOrWhiteSpace(clause))
                clauses.Add(clause);
        }

        if (clauses.Count == 0)
            return new SqlTranslationResult { WhereClause = null, Parameters = parameters };

        var whereClause = clauses.Count == 1 ? clauses[0] : string.Join(" AND ", clauses);
        return new SqlTranslationResult { WhereClause = whereClause, Parameters = parameters };
    }

    /// <inheritdoc />
    public SqlTranslationResult TranslateSingle(
        FilterExpression filter,
        IReadOnlyList<FieldDefinitionDto> knownFields)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Translate([filter], knownFields);
    }

    // ----------------------------------------------------------------
    // Core recursive translator
    // ----------------------------------------------------------------

    private static string? TranslateNode(
        FilterExpression node,
        Dictionary<string, object?> parameters,
        HashSet<string> knownFieldNames,
        Dictionary<string, string> fieldTypeMap,
        ref int paramIndex,
        int depth)
    {
        if (depth > MaxNestingDepth)
            throw new InvalidOperationException(
                $"Filter expression exceeds the maximum nesting depth of {MaxNestingDepth}.");

        return node switch
        {
            CompositeFilterExpression composite =>
                TranslateComposite(composite, parameters, knownFieldNames, fieldTypeMap, ref paramIndex, depth),
            LeafFilterExpression leaf =>
                TranslateLeaf(leaf, parameters, knownFieldNames, fieldTypeMap, ref paramIndex),
            _ => throw new InvalidOperationException($"Unknown FilterExpression type: {node.GetType().Name}")
        };
    }

    private static string? TranslateComposite(
        CompositeFilterExpression composite,
        Dictionary<string, object?> parameters,
        HashSet<string> knownFieldNames,
        Dictionary<string, string> fieldTypeMap,
        ref int paramIndex,
        int depth)
    {
        if (composite.Children is null || composite.Children.Count == 0)
            return null;

        var separator = composite.LogicalOperator == LogicalOperator.Or ? " OR " : " AND ";
        var childClauses = new List<string>();

        foreach (var child in composite.Children)
        {
            var childClause = TranslateNode(child, parameters, knownFieldNames, fieldTypeMap, ref paramIndex, depth + 1);
            if (!string.IsNullOrWhiteSpace(childClause))
                childClauses.Add(childClause);
        }

        return childClauses.Count switch
        {
            0 => null,
            1 => childClauses[0],
            _ => "(" + string.Join(separator, childClauses) + ")"
        };
    }

    private static string TranslateLeaf(
        LeafFilterExpression leaf,
        Dictionary<string, object?> parameters,
        HashSet<string> knownFieldNames,
        Dictionary<string, string> fieldTypeMap,
        ref int paramIndex)
    {
        if (string.IsNullOrWhiteSpace(leaf.FieldName))
            throw new InvalidOperationException("Leaf filter node has no FieldName.");

        // Validate field name against known schema (prevents column name injection)
        var upperField = leaf.FieldName.ToUpperInvariant();
        if (knownFieldNames.Count > 0 && !knownFieldNames.Contains(upperField))
        {
            throw new InvalidOperationException(
                $"Filter field '{leaf.FieldName}' is not declared in the data source schema.");
        }

        var quotedColumn = QuoteIdentifier(leaf.FieldName);
        var paramName = $"@p{paramIndex}";
        paramIndex++;

        // Handle null-check operators — no value parameter needed
        if (leaf.Operator == FilterOperator.IsNull)
            return $"{quotedColumn} IS NULL";

        if (leaf.Operator == FilterOperator.IsNotNull)
            return $"{quotedColumn} IS NOT NULL";

        // Handle IN operator — multiple parameter names
        if (leaf.Operator == FilterOperator.In)
        {
            var inValues = ExtractInValues(leaf.Value);
            if (inValues.Count > MaxInValues)
                throw new InvalidOperationException(
                    $"IN operator supports a maximum of {MaxInValues} values; {inValues.Count} were provided.");

            if (inValues.Count == 0)
                return "1=0";  // Empty IN list — no rows match, safe sentinel

            var inParams = new List<string>(inValues.Count);
            for (var i = 0; i < inValues.Count; i++)
            {
                var pn = $"{paramName}_{i}";
                parameters[pn] = inValues[i];
                inParams.Add(pn);
            }
            return $"{quotedColumn} IN ({string.Join(", ", inParams)})";
        }

        // For date fields with relative filter strings, resolve to concrete DateTime
        var fieldType = fieldTypeMap.TryGetValue(upperField, out var ft) ? ft : null;
        var resolvedValue = IsDateField(fieldType) && leaf.Value is string relativeStr
            ? ResolveRelativeDateFilter(relativeStr)
            : leaf.Value;

        var sqlOp = MapOperatorToSql(leaf.Operator);
        parameters[paramName] = resolvedValue;
        return $"{quotedColumn} {sqlOp} {paramName}";
    }

    // ----------------------------------------------------------------
    // Helpers: identifier quoting, operator mapping, field name validation
    // ----------------------------------------------------------------

    /// <summary>
    /// Wraps a column identifier in square brackets to prevent SQL injection for structural identifiers.
    /// </summary>
    private static string QuoteIdentifier(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>
    /// Maps the <see cref="FilterOperator"/> enum to the corresponding SQL operator string.
    /// </summary>
    private static string MapOperatorToSql(FilterOperator op) => op switch
    {
        FilterOperator.Equals => "=",
        FilterOperator.NotEquals => "<>",
        FilterOperator.GreaterThan => ">",
        FilterOperator.LessThan => "<",
        FilterOperator.GreaterThanOrEquals => ">=",
        FilterOperator.LessThanOrEquals => "<=",
        FilterOperator.Like => "LIKE",
        FilterOperator.In => "IN",
        FilterOperator.IsNull => "IS NULL",
        FilterOperator.IsNotNull => "IS NOT NULL",
        _ => throw new InvalidOperationException($"Unsupported FilterOperator: {op}")
    };

    private static HashSet<string> BuildFieldNameSet(IReadOnlyList<FieldDefinitionDto> fields)
        => fields.Select(f => f.FieldName.ToUpperInvariant()).ToHashSet();

    private static Dictionary<string, string> BuildFieldTypeMap(IReadOnlyList<FieldDefinitionDto> fields)
        => fields.ToDictionary(
            f => f.FieldName.ToUpperInvariant(),
            f => f.FieldType?.ToLowerInvariant() ?? string.Empty);

    private static bool IsDateField(string? fieldType)
        => fieldType is "datetime" or "date" or "datetime2" or "smalldatetime";

    private static IReadOnlyList<object?> ExtractInValues(object? value)
    {
        if (value is null) return [];
        if (value is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var list = new List<object?>();
            foreach (var element in je.EnumerateArray())
            {
                list.Add(element.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => element.GetString(),
                    System.Text.Json.JsonValueKind.Number when element.TryGetInt64(out var l) => (object?)l,
                    System.Text.Json.JsonValueKind.Number => element.GetDecimal(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    _ => element.GetRawText()
                });
            }
            return list;
        }
        if (value is System.Collections.IEnumerable enumerable and not string)
            return enumerable.Cast<object?>().ToList();
        return [value];
    }

    // ----------------------------------------------------------------
    // Relative date filter resolution (Business Rule 2)
    // ----------------------------------------------------------------

    /// <summary>
    /// Resolves a relative date filter keyword to a concrete UTC <see cref="DateTime"/>.
    /// Supported keywords: today, yesterday, last7days, last30days, last90days, last365days,
    /// thisweek, thismonth, thisyear.
    /// If the value is not a recognised keyword, it is returned unchanged for standard parsing.
    /// </summary>
    internal static object? ResolveRelativeDateFilter(string value)
    {
        var now = DateTime.UtcNow;
        return value.Trim().ToLowerInvariant() switch
        {
            "today" => now.Date,
            "yesterday" => now.Date.AddDays(-1),
            "last7days" => now.Date.AddDays(-7),
            "last30days" => now.Date.AddDays(-30),
            "last90days" => now.Date.AddDays(-90),
            "last365days" => now.Date.AddDays(-365),
            "thisweek" => now.Date.AddDays(-(int)now.DayOfWeek),
            "thismonth" => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "thisyear" => new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => value  // Return unchanged — may be a literal date string
        };
    }
}
