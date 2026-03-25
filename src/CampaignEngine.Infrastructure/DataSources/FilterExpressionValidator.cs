using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Filters;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Validates filter expression ASTs before SQL translation.
///
/// Validation is intentionally separate from translation so that the API layer
/// can return descriptive 400 errors before any SQL generation occurs.
/// </summary>
public sealed class FilterExpressionValidator : IFilterExpressionValidator
{
    private const int MaxNestingDepth = 5;
    private const int MaxInValues = 1000;

    /// <inheritdoc />
    public FilterValidationResult Validate(
        IReadOnlyList<FilterExpression> filters,
        IReadOnlyList<FieldDefinitionDto> knownFields)
    {
        var result = new FilterValidationResult();

        if (filters is null || filters.Count == 0)
            return result;  // Empty is valid — means "no filter"

        var filterableFields = BuildFilterableFieldSet(knownFields);
        var fieldTypeMap = BuildFieldTypeMap(knownFields);

        for (var i = 0; i < filters.Count; i++)
        {
            ValidateNode(filters[i], filterableFields, fieldTypeMap, result, depth: 0, path: $"[{i}]");
        }

        return result;
    }

    /// <inheritdoc />
    public FilterValidationResult ValidateSingle(
        FilterExpression filter,
        IReadOnlyList<FieldDefinitionDto> knownFields)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Validate([filter], knownFields);
    }

    // ----------------------------------------------------------------
    // Core recursive validator
    // ----------------------------------------------------------------

    private static void ValidateNode(
        FilterExpression node,
        HashSet<string> filterableFields,
        Dictionary<string, string> fieldTypeMap,
        FilterValidationResult result,
        int depth,
        string path)
    {
        if (depth > MaxNestingDepth)
        {
            result.AddError($"{path}: Nesting depth exceeds maximum of {MaxNestingDepth}.");
            return;  // Stop descending — we already know this subtree is too deep
        }

        switch (node)
        {
            case CompositeFilterExpression composite:
                ValidateComposite(composite, filterableFields, fieldTypeMap, result, depth, path);
                break;
            case LeafFilterExpression leaf:
                ValidateLeaf(leaf, filterableFields, fieldTypeMap, result, path);
                break;
            default:
                result.AddError($"{path}: Unknown FilterExpression type '{node.GetType().Name}'.");
                break;
        }
    }

    private static void ValidateComposite(
        CompositeFilterExpression composite,
        HashSet<string> filterableFields,
        Dictionary<string, string> fieldTypeMap,
        FilterValidationResult result,
        int depth,
        string path)
    {
        if (composite.Children is null || composite.Children.Count == 0)
        {
            result.AddError($"{path}: Composite filter must have at least one child expression.");
            return;
        }

        if (!Enum.IsDefined(composite.LogicalOperator))
            result.AddError($"{path}: Unknown LogicalOperator value '{composite.LogicalOperator}'.");

        for (var i = 0; i < composite.Children.Count; i++)
        {
            ValidateNode(
                composite.Children[i], filterableFields, fieldTypeMap, result,
                depth + 1, $"{path}.children[{i}]");
        }
    }

    private static void ValidateLeaf(
        LeafFilterExpression leaf,
        HashSet<string> filterableFields,
        Dictionary<string, string> fieldTypeMap,
        FilterValidationResult result,
        string path)
    {
        // FieldName must not be empty
        if (string.IsNullOrWhiteSpace(leaf.FieldName))
        {
            result.AddError($"{path}: FieldName is required on leaf filter nodes.");
            return;  // Cannot proceed with further field-based checks
        }

        // FieldName must be in the declared schema
        var upperField = leaf.FieldName.ToUpperInvariant();
        if (filterableFields.Count > 0 && !filterableFields.Contains(upperField))
        {
            result.AddError($"{path}: Field '{leaf.FieldName}' is not declared in the data source schema or is not filterable.");
        }

        // Operator must be a defined enum value
        if (!Enum.IsDefined(leaf.Operator))
        {
            result.AddError($"{path}: Operator value '{(int)leaf.Operator}' is not a recognised FilterOperator.");
            return;
        }

        // Operator-specific value validation
        switch (leaf.Operator)
        {
            case FilterOperator.IsNull:
            case FilterOperator.IsNotNull:
                // No value required — silently accept even if value is provided (it will be ignored)
                break;

            case FilterOperator.In:
                ValidateInOperator(leaf, result, path);
                break;

            default:
                // All other operators require a non-null value
                if (leaf.Value is null)
                    result.AddError($"{path}: Operator '{leaf.Operator}' requires a non-null value.");
                break;
        }

        // Date field value validation
        var fieldType = fieldTypeMap.TryGetValue(upperField, out var ft) ? ft : null;
        if (IsDateField(fieldType) && leaf.Value is string dateStr)
        {
            ValidateDateValue(dateStr, result, path);
        }
    }

    private static void ValidateInOperator(
        LeafFilterExpression leaf,
        FilterValidationResult result,
        string path)
    {
        if (leaf.Value is null)
        {
            result.AddError($"{path}: IN operator requires a non-null list of values.");
            return;
        }

        int count;
        if (leaf.Value is System.Text.Json.JsonElement je
            && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            count = je.GetArrayLength();
        }
        else if (leaf.Value is System.Collections.IEnumerable enumerable and not string)
        {
            count = enumerable.Cast<object?>().Count();
        }
        else
        {
            // Single value — treated as list of one; valid
            return;
        }

        if (count == 0)
            result.AddError($"{path}: IN operator requires at least one value.");

        if (count > MaxInValues)
            result.AddError($"{path}: IN operator supports a maximum of {MaxInValues} values; {count} were provided.");
    }

    private static void ValidateDateValue(string value, FilterValidationResult result, string path)
    {
        var knownRelative = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "today", "yesterday", "last7days", "last30days",
            "last90days", "last365days", "thisweek", "thismonth", "thisyear"
        };

        if (knownRelative.Contains(value))
            return;  // Recognised relative keyword — valid

        // Check if it parses as a literal date
        if (!DateTime.TryParse(value, out _))
        {
            result.AddError(
                $"{path}: Value '{value}' is not a valid date or recognised relative date keyword " +
                "(e.g., 'today', 'last30days', 'thismonth').");
        }
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static HashSet<string> BuildFilterableFieldSet(IReadOnlyList<FieldDefinitionDto> fields)
        => fields
            .Where(f => f.IsFilterable)
            .Select(f => f.FieldName.ToUpperInvariant())
            .ToHashSet();

    private static Dictionary<string, string> BuildFieldTypeMap(IReadOnlyList<FieldDefinitionDto> fields)
        => fields.ToDictionary(
            f => f.FieldName.ToUpperInvariant(),
            f => f.FieldType?.ToLowerInvariant() ?? string.Empty);

    private static bool IsDateField(string? fieldType)
        => fieldType is "datetime" or "date" or "datetime2" or "smalldatetime";
}
