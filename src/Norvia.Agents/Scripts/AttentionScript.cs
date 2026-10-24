using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted answer to "what needs attention": it lists the dead-letter queues, peeks each that holds messages (the oldest
/// first, as many as the tool-call cap allows) and groups what it read by dead-letter reason.
/// </summary>
internal static class AttentionScript
{
    // The definition caps a question at six tool calls: the list and five peeks.
    private const int MaxPeeks = 5;

    public static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (context.Results.Count == 0)
        {
            return [new FunctionCallContent($"call-{context.Turn}", IntegrationOperationsScript.ListDeadLetterQueues)];
        }

        var listed = context.Results[0];
        var held = NonEmpty(listed.Content);
        if (held.Count > 0 && context.Results.Count == 1)
        {
            return [.. held.Take(MaxPeeks).Select((queue, index) =>
                new FunctionCallContent($"call-{context.Turn}-{index}", IntegrationOperationsScript.PeekDeadLetters, new Dictionary<string, object?> { ["entity"] = queue }))];
        }

        return [new TextContent(held.Count == 0 ? NothingHeld(listed.Id) : Grouped(held, context.Results.Skip(1).ToList()))];
    }

    private static string NothingHeld(string listedId) =>
        IntegrationOperationsScript.Json(
            "No dead-letter queue holds a message, so nothing needs attention.",
            [("Every queue and subscription listed has a dead-letter count of zero.", listedId)],
            "Nothing to do. Check again if a flow reports a failure.",
            "high");

    private static string Grouped(List<string> held, List<ScriptedToolResult> peeks)
    {
        var groups = new List<Group>();
        foreach (var (queue, peek) in held.Take(MaxPeeks).Zip(peeks))
        {
            foreach (var reason in ReasonsOf(peek.Content))
            {
                var group = groups.Find(existing => existing.Reason == reason.Reason);
                if (group is null)
                {
                    groups.Add(group = new Group(reason.Reason, peek.Id));
                }

                group.Messages += reason.Count;
                if (!group.Queues.Contains(queue))
                {
                    group.Queues.Add(queue);
                }
            }
        }

        var unread = held.Count > MaxPeeks ? $" {held.Count - MaxPeeks} more queues hold messages that were not read." : string.Empty;
        var evidence = groups.Select(group => ($"{group.Messages} message(s) were dead-lettered with the reason '{group.Reason}' on {string.Join(", ", group.Queues)}.", group.Citation)).ToList();
        var cause = groups.Count == 0
            ? "The queues that hold messages showed no message to read."
            : $"{held.Count} queue(s) hold dead-lettered messages, in {groups.Count} group(s) by cause: {string.Join("; ", groups.Select(group => $"{group.Reason} ({group.Messages} on {string.Join(", ", group.Queues)})"))}.{unread}";
        if (evidence.Count == 0)
        {
            evidence.Add(("The peeks returned no messages.", peeks.Count > 0 ? peeks[0].Id : "r1"));
        }

        return IntegrationOperationsScript.Json(
            cause,
            evidence,
            "Start with the group that has the most messages: fix its cause, then resubmit its messages from the queue's page, or discard them once they are no longer wanted.",
            groups.Count == 0 ? "low" : unread.Length > 0 ? "medium" : "high");
    }

    // The queues and subscriptions that hold a message, the oldest first as the tool lists them.
    private static List<string> NonEmpty(string content)
    {
        var held = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("queues", out var queues) && queues.ValueKind == JsonValueKind.Array)
            {
                foreach (var queue in queues.EnumerateArray())
                {
                    if (queue.TryGetProperty("count", out var count) && count.TryGetInt64(out var messages) && messages > 0 && IntegrationOperationsScript.Text(queue, "name") is { } name)
                    {
                        held.Add(name);
                    }
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return held;
    }

    private static IEnumerable<(string Reason, int Count)> ReasonsOf(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. messages.EnumerateArray().GroupBy(message => IntegrationOperationsScript.Text(message, "reason") ?? "no reason given").Select(group => (group.Key, group.Count()))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class Group(string reason, string citation)
    {
        public string Reason { get; } = reason;

        public string Citation { get; } = citation;

        public int Messages { get; set; }

        public List<string> Queues { get; } = [];
    }
}
