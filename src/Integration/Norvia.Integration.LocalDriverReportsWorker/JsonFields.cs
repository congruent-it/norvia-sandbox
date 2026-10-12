using System.Text.Json.Nodes;

namespace Norvia.Integration.LocalDriverReportsWorker;

internal static class JsonFields
{
    /// <summary>A field of a JSON object read leniently: anything that isn't non-blank text reads as absent.</summary>
    public static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
