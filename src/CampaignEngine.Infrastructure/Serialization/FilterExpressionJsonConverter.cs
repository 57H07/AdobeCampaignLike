using System.Text.Json;
using System.Text.Json.Serialization;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Filters;

namespace CampaignEngine.Infrastructure.Serialization;

/// <summary>
/// Custom System.Text.Json converter for <see cref="FilterExpression"/> polymorphic
/// serialization. Uses a "type" discriminator field written explicitly during serialization,
/// and read back during deserialization to select the correct concrete subclass.
///
/// Moved to Infrastructure so that Domain filter classes remain free of framework attributes.
/// </summary>
public sealed class FilterExpressionJsonConverter : JsonConverter<FilterExpression>
{
    public override FilterExpression Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Buffer the entire JSON object so we can peek at the "type" discriminator first
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("FilterExpression JSON is missing the required 'type' discriminator.");

        var nodeType = typeProp.GetString()?.ToLowerInvariant();
        var json = root.GetRawText();

        // Build options without this converter to avoid infinite recursion when deserializing
        // the concrete subtypes (LeafFilterExpression / CompositeFilterExpression).
        var innerOptions = BuildInnerOptions(options);

        return nodeType switch
        {
            "leaf" => JsonSerializer.Deserialize<LeafFilterExpression>(json, innerOptions)
                      ?? throw new JsonException("Failed to deserialize LeafFilterExpression."),
            "composite" => JsonSerializer.Deserialize<CompositeFilterExpression>(json, innerOptions)
                           ?? throw new JsonException("Failed to deserialize CompositeFilterExpression."),
            _ => throw new JsonException($"Unknown FilterExpression node type: '{nodeType}'. Expected 'leaf' or 'composite'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        FilterExpression value,
        JsonSerializerOptions options)
    {
        // Write each concrete type manually so we can emit the "type" discriminator field
        // without relying on [JsonPropertyName] attributes on the domain class.
        var innerOptions = BuildInnerOptions(options);

        switch (value)
        {
            case LeafFilterExpression leaf:
                writer.WriteStartObject();
                writer.WriteString("type", leaf.NodeType);
                writer.WriteString("fieldName", leaf.FieldName);
                writer.WritePropertyName("operator");
                JsonSerializer.Serialize(writer, leaf.Operator, innerOptions);
                if (leaf.Value is not null)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, leaf.Value, innerOptions);
                }
                writer.WriteEndObject();
                break;

            case CompositeFilterExpression composite:
                writer.WriteStartObject();
                writer.WriteString("type", composite.NodeType);
                writer.WritePropertyName("logicalOperator");
                JsonSerializer.Serialize(writer, composite.LogicalOperator, innerOptions);
                writer.WritePropertyName("children");
                JsonSerializer.Serialize(writer, composite.Children, innerOptions);
                writer.WriteEndObject();
                break;

            default:
                throw new JsonException($"Unsupported FilterExpression type: {value.GetType().Name}");
        }
    }

    /// <summary>
    /// Builds serializer options that exclude this converter to prevent infinite recursion
    /// when serializing/deserializing concrete subtypes internally.
    /// </summary>
    private JsonSerializerOptions BuildInnerOptions(JsonSerializerOptions options)
    {
        var inner = new JsonSerializerOptions(options);
        var toRemove = inner.Converters.OfType<FilterExpressionJsonConverter>().FirstOrDefault();
        if (toRemove is not null)
            inner.Converters.Remove(toRemove);
        return inner;
    }
}
