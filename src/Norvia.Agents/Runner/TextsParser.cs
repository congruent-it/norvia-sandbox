using System.Text.Json;

namespace Norvia.Agents.Runner;

/// <summary>
/// Reads the model's final reply as a list of texts: one JSON object with a <c>texts</c> array of strings. A reply that is anything else is
/// <see cref="AgentFailureCodes.AgentUnavailable"/>; the caller decides which of the texts it can use.
/// </summary>
public static class TextsParser
{
    public static IReadOnlyList<string> Parse(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        try
        {
            using var document = JsonDocument.Parse(JsonReplies.WithoutFence(reply));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("texts", out var texts)
                || texts.ValueKind != JsonValueKind.Array
                || texts.EnumerateArray().Any(text => text.ValueKind != JsonValueKind.String))
            {
                throw new AgentRunException("The reply isn't a list of texts.");
            }

            return [.. texts.EnumerateArray().Select(text => text.GetString()!)];
        }
        catch (JsonException exception)
        {
            throw new AgentRunException("The reply isn't JSON.", exception);
        }
    }
}
