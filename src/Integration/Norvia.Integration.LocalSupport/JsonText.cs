using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Integration.LocalSupport;

/// <summary>Reads one text field of a JSON object leniently, as the stand-ins do: anything that isn't a JSON object with that field as text reads as absent.</summary>
public static class JsonText
{
    public static string? Field(string json, string name) => Read(() => JsonNode.Parse(json), name);

    public static string? Field(byte[] json, string name) => Read(() => JsonNode.Parse(json), name);

    private static string? Read(Func<JsonNode?> parse, string name)
    {
        try
        {
            return parse() is JsonObject message && message[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
