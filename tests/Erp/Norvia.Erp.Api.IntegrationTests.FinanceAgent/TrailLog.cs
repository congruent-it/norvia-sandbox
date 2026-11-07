using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>One log an application wrote with the JSON console formatter, with the keys of every scope it was written in.</summary>
/// <param name="Text">Every value in the log, unescaped, to search for what must never be logged.</param>
public sealed record TrailLog(IReadOnlyDictionary<string, string?> Scope, string Text)
{
    public string? this[string key] => Scope.GetValueOrDefault(key);

    public static TrailLog? TryParse(string line)
    {
        var start = line.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(line[start..]) is not JsonObject log)
            {
                return null;
            }

            var scope = new Dictionary<string, string?>();
            foreach (var entry in log["Scopes"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                foreach (var (key, value) in entry.Where(pair => pair.Key != "Message"))
                {
                    scope[key] = value?.ToString();
                }
            }

            return new TrailLog(scope, string.Join('\n', ValuesOf(log)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ValuesOf(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(pair => ValuesOf(pair.Value)),
        JsonArray a => a.SelectMany(ValuesOf),
        JsonValue v => [v.ToString()],
        _ => [],
    };
}
