using System.Text.Json;
using System.Text.Json.Serialization;
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
///
/// JSON Serialization:
///   Uses System.Text.Json with a custom converter so the concrete type
///   is preserved across serialize/deserialize cycles via the "type" discriminator.
/// </summary>
[JsonConverter(typeof(FilterExpressionJsonConverter))]
public abstract class FilterExpression
{
    /// <summary>
    /// Discriminator used for JSON serialization to distinguish composite vs. leaf nodes.
    /// </summary>
    [JsonPropertyName("type")]
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

    // ----------------------------------------------------------------
    // JSON round-trip helpers
    // ----------------------------------------------------------------

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes the AST to a compact JSON string for storage.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, GetType(), _serializerOptions);

    /// <summary>
    /// Deserializes a JSON string back into a FilterExpression AST.
    /// </summary>
    /// <exception cref="JsonException">Thrown when the JSON is not a valid FilterExpression.</exception>
    public static FilterExpression FromJson(string json)
        => JsonSerializer.Deserialize<FilterExpression>(json, _serializerOptions)
           ?? throw new JsonException("Deserialized FilterExpression is null.");

    /// <summary>
    /// Serializes a list of top-level filter expressions to JSON.
    /// </summary>
    public static string ListToJson(IReadOnlyList<FilterExpression> filters)
        => JsonSerializer.Serialize(filters, _serializerOptions);

    /// <summary>
    /// Deserializes a JSON array of top-level filter expressions.
    /// </summary>
    public static IReadOnlyList<FilterExpression> ListFromJson(string json)
        => JsonSerializer.Deserialize<IReadOnlyList<FilterExpression>>(json, _serializerOptions)
           ?? [];
}
