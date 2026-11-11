using System.Text.Json.Nodes;
using Norvia.Agents.Tools;

namespace Norvia.Integration.LocalAgentMcp;

/// <summary>The tools of the stand-in's one MCP server as the MCP messages describe them.</summary>
internal static class McpTools
{
    public static JsonObject Describe(ToolSpec tool)
    {
        var properties = new JsonObject();
        foreach (var parameter in tool.Parameters)
        {
            properties[parameter] = new JsonObject { ["type"] = "string" };
        }

        return new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray([.. tool.Parameters.Select(parameter => (JsonNode)JsonValue.Create(parameter)!)]),
                ["additionalProperties"] = false,
            },
        };
    }

    /// <summary>The route of a call on the agent view: the tool's path with each input in its segment, or null when an input is missing or not a plain identifier.</summary>
    public static string? PathOf(ToolSpec tool, JsonObject? arguments)
    {
        var path = tool.Path;
        foreach (var parameter in tool.Parameters)
        {
            if (arguments?[parameter] is not JsonValue value || !value.TryGetValue<string>(out var text) || !Plain(parameter, text))
            {
                return null;
            }

            path = path.Replace("{" + parameter + "}", Uri.EscapeDataString(text), StringComparison.Ordinal);
        }

        return path;
    }

    // Business keys, correlation IDs and run IDs are identifiers; nothing else reaches a URL. A queue or subscription's name
    // (entity) may also hold the '/' of topic/subscription, and then only between two names. The what-if tally (alreadyAssumed) is a comma-separated list of order numbers.
    private static bool Plain(string parameter, string text) =>
        text.Length is > 0 and <= 200
        && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':' || (character == '/' && parameter == "entity") || (character == ',' && parameter == "alreadyAssumed"))
        && !text.Split('/').Any(name => name.Length == 0 || name.All(character => character == '.'));
}
