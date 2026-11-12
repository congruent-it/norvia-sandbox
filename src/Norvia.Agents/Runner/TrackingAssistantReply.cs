using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Agents.Runner;

/// <summary>What the Shipper tracking assistant answered (ADR 0049): the text, the field of the tracking data behind each claim, and whether it has no information.</summary>
public sealed record TrackingAssistantReply(string Answer, IReadOnlyList<string> Fields, bool NoInformation);

/// <summary>Reads the assistant's reply. A reply that is not that object is <see cref="AgentFailureCodes.AgentUnavailable"/>.</summary>
public static class TrackingAssistantReplies
{
    public static TrackingAssistantReply Parse(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        try
        {
            var json = JsonNode.Parse(JsonReplies.WithoutFence(reply)) as JsonObject ?? throw new AgentRunException("The reply is not a JSON object.");
            var answer = json["answer"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new AgentRunException("The reply has no answer.");
            }

            var fields = (json["claims"]?.AsArray() ?? []).Select(claim => claim?["field"]?.GetValue<string>() ?? throw new AgentRunException("A claim has no field.")).ToList();
            return new TrackingAssistantReply(answer, fields, json["noInformation"]?.GetValue<bool>() ?? false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new AgentRunException("The reply is not the assistant's result.", exception);
        }
    }
}
