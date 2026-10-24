using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Integration operations agent. It reads the Message trail and, for a failed hop that was a Logic App
/// run, the run; then it answers. For the P5 trail, where a Carrier sent <c>DLV</c> instead of <c>POD</c> and the
/// Carrier-tracking flow dead-lettered the message, the answer is the one the demo tells. Any other failed hop gets an answer
/// built from the hop's own outcome and failure code, and a trail with no failed hop is told so.
/// </summary>
public static class IntegrationOperationsScript
{
    public const string GetMessageTrail = "get_message_trail";
    public const string GetRun = "get_run";
    public const string ListFailedRuns = "list_failed_runs";
    public const string ListDeadLetterQueues = "list_dead_letter_queues";
    public const string PeekDeadLetters = "peek_dead_letters";

    /// <summary>The failure code the Carrier-tracking flow gives a tracking message whose status code it can't map (P5).</summary>
    public const string UnmappableTrackingMessage = "UnmappableTrackingMessage";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (AgentQuestions.ParseFailedRun(context.Question) is { } failedRun)
        {
            return FailedRunScript.Next(context, failedRun.Flow, failedRun.Start);
        }

        if (AgentQuestions.ParseDeadLetter(context.Question) is { } deadLetter)
        {
            return DeadLetterScript.Next(context, deadLetter.Entity, deadLetter.SequenceNumber);
        }

        if (AgentQuestions.IsWhatNeedsAttention(context.Question))
        {
            return AttentionScript.Next(context);
        }

        if (context.Results.Count == 0)
        {
            return [new FunctionCallContent($"call-{context.Turn}", GetMessageTrail, new Dictionary<string, object?> { ["correlationId"] = context.Input("correlationId") })];
        }

        var trail = context.Results[0];
        var failed = FirstFailedHop(trail.Content);
        if (failed is { Run: { } run } && context.Results.Count == 1)
        {
            return
            [
                new FunctionCallContent(
                    $"call-{context.Turn}",
                    GetRun,
                    new Dictionary<string, object?> { ["logicApp"] = run.LogicApp, ["workflow"] = run.Workflow, ["runId"] = run.RunId }),
            ];
        }

        return [new TextContent(Answer(trail.Id, failed, context.Results.Count > 1 ? context.Results[1] : null))];
    }

    private static string Answer(string trailId, FailedHop? failed, ScriptedToolResult? run)
    {
        if (failed is null)
        {
            return Json(
                "No hop on this Message trail has failed, so there is nothing to explain.",
                [("None of the hops recorded an outcome of DeadLettered or Failed.", trailId)],
                "Check the trail again in a few minutes: telemetry can be minutes behind, and the failing hop may not be recorded yet.",
                "low",
                "needs-a-person");
        }

        var evidence = new List<(string Claim, string Citation)>
        {
            ($"The hop '{failed.Name}' ended {failed.Outcome}{(failed.FailureCode is null ? string.Empty : $" with failure code {failed.FailureCode}")}.", trailId),
        };
        if (run is not null)
        {
            evidence.Add(($"The run of {failed.Run!.Workflow} is recorded with the steps and errors the second result shows.", run.Id));
        }

        return failed.FailureCode == UnmappableTrackingMessage
            ? Json(
                "The Carrier-tracking flow could not map the status code in the Carrier's tracking message, so it dead-lettered the message. The Carrier was still answered 202, so the Shipment stays In transit and nobody was told.",
                evidence,
                "Ask the Carrier to send the Delivered event again with a status code the map knows (POD, not DLV). Then resubmit the dead-lettered message from the Dead-letter queues page, or discard it once the Carrier's resend has arrived.",
                "high",
                "fix-the-cause-first")
            : Json(
                $"The hop '{failed.Name}' ended {failed.Outcome}{(failed.FailureCode is null ? string.Empty : $" with failure code {failed.FailureCode}")}.",
                evidence,
                "Open the hop's run or its dead-letter queue from this page to see what failed, and fix its cause before you resubmit anything.",
                "medium",
                "fix-the-cause-first");
    }

    internal static string Json(string cause, IEnumerable<(string Claim, string Citation)> evidence, string action, string confidence, string kind) =>
        JsonSerializer.Serialize(new
        {
            cause,
            evidence = evidence.Select(item => new { claim = item.Claim, citation = item.Citation }),
            action = kind,
            suggestedAction = action,
            confidence,
        });

    // The first hop that failed, as the agent view tells it.
    private static FailedHop? FirstFailedHop(string trail)
    {
        try
        {
            using var document = JsonDocument.Parse(trail);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("hops", out var hops) || hops.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var hop in hops.EnumerateArray())
            {
                var outcome = Text(hop, "outcome");
                if (outcome is "DeadLettered" or "Failed")
                {
                    return new FailedHop(Text(hop, "name") ?? "a hop", outcome, Text(hop, "failureCode"), RunOf(hop));
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static RunRef? RunOf(JsonElement hop) =>
        hop.TryGetProperty("run", out var run) && run.ValueKind == JsonValueKind.Object
            && Text(run, "logicApp") is { } logicApp && Text(run, "workflow") is { } workflow && Text(run, "runId") is { } runId
                ? new RunRef(logicApp, workflow, runId)
                : null;

    internal static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record FailedHop(string Name, string Outcome, string? FailureCode, RunRef? Run);

    private sealed record RunRef(string LogicApp, string Workflow, string RunId);
}
