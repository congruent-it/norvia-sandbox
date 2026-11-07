using System.Text.Json;

namespace Norvia.Agents.Runner;

/// <summary>Reads the model's final reply as a <see cref="Suggestion"/>, and refuses one that isn't the expected result or cites a result the run never had.</summary>
public static class SuggestionParser
{
    public static Suggestion Parse(string reply, IReadOnlyDictionary<string, string> toolNamesByResultId)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(JsonReplies.WithoutFence(reply));
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new AgentRunException("The reply isn't JSON.", exception);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new AgentRunException("The reply isn't a Suggestion.");
        }

        var cause = Text(root, "cause");
        var action = Text(root, "suggestedAction");
        var kind = Text(root, "action") switch
        {
            "resubmit" => SuggestedActionKind.Resubmit,
            "discard" => SuggestedActionKind.Discard,
            "fix-the-cause-first" => SuggestedActionKind.FixTheCauseFirst,
            "release" => SuggestedActionKind.Release,
            "wait" => SuggestedActionKind.Wait,
            "accept" => SuggestedActionKind.Accept,
            "accept-adjusted" => SuggestedActionKind.AcceptAdjusted,
            "reject" => SuggestedActionKind.Reject,
            "needs-a-person" => SuggestedActionKind.NeedsAPerson,
            _ => throw new AgentRunException("The reply's action is not resubmit, discard, fix-the-cause-first, release, wait, accept, accept-adjusted, reject or needs-a-person."),
        };
        if (!Enum.TryParse<Confidence>(Text(root, "confidence"), ignoreCase: true, out var confidence))
        {
            throw new AgentRunException("The reply's confidence is not low, medium or high.");
        }

        if (!root.TryGetProperty("evidence", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
        {
            throw new AgentRunException("The reply cites no evidence.");
        }

        var evidence = new List<Evidence>();
        foreach (var item in items.EnumerateArray())
        {
            var resultId = item.ValueKind == JsonValueKind.Object ? Text(item, "citation") : string.Empty;
            if (!toolNamesByResultId.TryGetValue(resultId, out var toolName))
            {
                throw new AgentRunException($"The reply cites '{resultId}', which is no result of this run.");
            }

            evidence.Add(new Evidence(Text(item, "claim"), resultId, toolName));
        }

        return new Suggestion(cause, evidence, action, confidence, kind);
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : throw new AgentRunException($"The reply has no {property}.");
}
