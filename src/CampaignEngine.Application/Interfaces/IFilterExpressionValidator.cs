using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Domain.Filters;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Validates filter expression ASTs before SQL translation.
///
/// Validation rules:
///   1. Leaf nodes must have a non-empty FieldName.
///   2. Leaf nodes must reference a declared, filterable field in the schema.
///   3. The operator must be a valid <see cref="CampaignEngine.Domain.Enums.FilterOperator"/> value.
///   4. IsNull/IsNotNull operators must have no value (value is ignored with a warning).
///   5. IN operator must have a non-null, non-empty list of values (up to 1000).
///   6. Composite nodes must have at least one child.
///   7. Nesting depth must not exceed 5.
///   8. Value types must be compatible with the declared field type.
/// </summary>
public interface IFilterExpressionValidator
{
    /// <summary>
    /// Validates a list of filter expressions against the declared schema fields.
    /// </summary>
    /// <returns>A validation result containing any errors found.</returns>
    FilterValidationResult Validate(
        IReadOnlyList<FilterExpression> filters,
        IReadOnlyList<FieldDefinitionDto> knownFields);

    /// <summary>
    /// Validates a single filter expression.
    /// </summary>
    FilterValidationResult ValidateSingle(
        FilterExpression filter,
        IReadOnlyList<FieldDefinitionDto> knownFields);
}

/// <summary>
/// Result of a filter expression validation.
/// </summary>
public sealed class FilterValidationResult
{
    private readonly List<string> _errors = [];

    /// <summary>True when no validation errors were found.</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>All validation error messages.</summary>
    public IReadOnlyList<string> Errors => _errors;

    public void AddError(string error) => _errors.Add(error);

    /// <summary>Creates a successful (empty) validation result.</summary>
    public static FilterValidationResult Ok() => new();

    /// <summary>Creates a result with a single error.</summary>
    public static FilterValidationResult Fail(string error)
    {
        var result = new FilterValidationResult();
        result.AddError(error);
        return result;
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> when validation failed.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new InvalidOperationException(
                "Filter expression validation failed: " + string.Join("; ", _errors));
    }
}
