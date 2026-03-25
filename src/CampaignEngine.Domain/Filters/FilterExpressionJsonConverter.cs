using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampaignEngine.Domain.Filters;

/// <summary>
/// Custom System.Text.Json converter for <see cref="FilterExpression"/> polymorphic deserialization.
/// Uses the "type" discriminator property to determine whether to instantiate a
/// <see cref="LeafFilterExpression"/> or a <see cref="CompositeFilterExpression"/>.
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

        return nodeType switch
        {
            "leaf" => JsonSerializer.Deserialize<LeafFilterExpression>(json, options)
                      ?? throw new JsonException("Failed to deserialize LeafFilterExpression."),
            "composite" => JsonSerializer.Deserialize<CompositeFilterExpression>(json, options)
                           ?? throw new JsonException("Failed to deserialize CompositeFilterExpression."),
            _ => throw new JsonException($"Unknown FilterExpression node type: '{nodeType}'. Expected 'leaf' or 'composite'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        FilterExpression value,
        JsonSerializerOptions options)
    {
        switch (value)
        {
            case LeafFilterExpression leaf:
                JsonSerializer.Serialize(writer, leaf, options);
                break;
            case CompositeFilterExpression composite:
                JsonSerializer.Serialize(writer, composite, options);
                break;
            default:
                throw new JsonException($"Unsupported FilterExpression type: {value.GetType().Name}");
        }
    }
}
