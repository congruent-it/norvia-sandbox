using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Runner;

/// <summary>Turns what a tool returned into the delimited text the model reads: data, never instructions.</summary>
internal static class ToolResults
{
    private const string Close = "</tool_result";

    public static string Delimit(string resultId, string toolName, string content) =>
        $"<tool_result id=\"{resultId}\" tool=\"{toolName}\">\n{content.Replace(Close, "<\\/tool_result", StringComparison.OrdinalIgnoreCase)}\n</tool_result>";

    /// <summary>
    /// The text of a tool's answer. The MCP client hands over its content blocks (an error among them is a refusal); a function
    /// made in code hands over a value or a JSON call result whose <c>content</c> is a list of text items, an <c>isError</c> one a refusal.
    /// </summary>
    public static string TextOf(object? raw, out bool isError)
    {
        isError = false;
        switch (raw)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case AIContent content:
                return TextOf(new[] { content }.AsEnumerable(), out isError);
            case IEnumerable<AIContent> contents:
                var items = contents.ToList();
                isError = items.OfType<ErrorContent>().Any();
                return string.Concat(items.OfType<TextContent>().Select(item => item.Text));
            case JsonElement { ValueKind: JsonValueKind.Object } element when element.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array:
                isError = element.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True;
                var builder = new StringBuilder();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var itemText) && itemText.GetString() is { } value)
                    {
                        builder.Append(value);
                    }
                }

                return builder.ToString();
            case JsonElement { ValueKind: JsonValueKind.String } stringElement:
                return stringElement.GetString() ?? string.Empty;
            case JsonElement other:
                return other.GetRawText();
            default:
                return JsonSerializer.Serialize(raw);
        }
    }
}
