using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>Reads a partner's JSON message leniently, as the Carrier maps do: a value of the wrong kind reads as absent.</summary>
internal static class JsonFields
{
    /// <summary>Parses JSON text, reading text that isn't a JSON object as absent.</summary>
    public static JsonObject? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    /// <summary>Removes the properties that have no value, so a value the map can't read is left out, never sent empty.</summary>
    public static JsonObject WithoutNulls(JsonObject node)
    {
        foreach (var name in node.Where(property => property.Value is null).Select(property => property.Key).ToList())
        {
            node.Remove(name);
        }

        return node;
    }
}
