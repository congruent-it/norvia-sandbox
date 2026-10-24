using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted answer to "explain this failed run": it reads the failed runs, finds the one asked about by its flow and start,
/// reads the Logic App run when there is one, and tells the failing step and its error. A run no longer in the list is said to be gone.
/// </summary>
internal static class FailedRunScript
{
    public static IReadOnlyList<AIContent> Next(ScriptContext context, string flow, DateTimeOffset start)
    {
        if (context.Results.Count == 0)
        {
            return [new FunctionCallContent($"call-{context.Turn}", IntegrationOperationsScript.ListFailedRuns)];
        }

        var listed = context.Results[0];
        var found = Find(listed.Content, flow, start);
        if (found is { RunReference: { } run } && context.Results.Count == 1)
        {
            return [new FunctionCallContent($"call-{context.Turn}", IntegrationOperationsScript.GetRun, new Dictionary<string, object?> { ["logicApp"] = run.LogicApp, ["workflow"] = run.Workflow, ["runId"] = run.RunId })];
        }

        return [new TextContent(Answer(listed.Id, flow, found, context.Results.Count > 1 ? context.Results[1] : null))];
    }

    private static string Answer(string listedId, string flow, FailedRun? found, ScriptedToolResult? run)
    {
        if (found is null)
        {
            return IntegrationOperationsScript.Json(
                $"The failed {flow} run isn't among the failed runs of the last 24 hours any more, so there is nothing to read about it.",
                [("The list of failed runs has no run of that flow that started then.", listedId)],
                "Open the Message trail of the run, if it tracked one, or look for the run in the Logic App's run history.",
                "low");
        }

        var failure = found.Step is null ? "a step the source doesn't name" : $"the step '{found.Step}'";
        var error = found.ErrorCode is null ? string.Empty : $" with the error {found.ErrorCode}{(found.ErrorMessage is null ? string.Empty : $": {found.ErrorMessage}")}";
        var evidence = new List<(string Claim, string Citation)> { ($"The {flow} run failed at {failure}{error}.", listedId) };
        if (run is not null)
        {
            evidence.Add(("The run's own steps and errors are the second result's.", run.Id));
        }

        return IntegrationOperationsScript.Json(
            $"The {flow} run failed at {failure}{error}.",
            evidence,
            found.CorrelationId is null
                ? "Fix what that step reports, then run the flow again from its source. The run tracked no Message trail, so there is none to open."
                : $"Open the Message trail {found.CorrelationId} to see what came before the failure, and fix what the step reports before anything is resubmitted.",
            found.ErrorCode is null ? "low" : "medium");
    }

    private static FailedRun? Find(string content, string flow, DateTimeOffset start)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var run in runs.EnumerateArray())
            {
                if (IntegrationOperationsScript.Text(run, "flow") != flow
                    || !DateTimeOffset.TryParse(IntegrationOperationsScript.Text(run, "start"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var started)
                    || started != start)
                {
                    continue;
                }

                var reference = run.TryGetProperty("run", out var logicApp) && logicApp.ValueKind == JsonValueKind.Object
                    && IntegrationOperationsScript.Text(logicApp, "logicApp") is { } app && IntegrationOperationsScript.Text(logicApp, "workflow") is { } workflow && IntegrationOperationsScript.Text(logicApp, "runId") is { } runId
                        ? new RunRef(app, workflow, runId)
                        : null;
                var error = run.TryGetProperty("error", out var failure) && failure.ValueKind == JsonValueKind.Object ? failure : (JsonElement?)null;
                return new FailedRun(
                    IntegrationOperationsScript.Text(run, "failedStep"),
                    error is { } e ? IntegrationOperationsScript.Text(e, "code") : null,
                    error is { } m ? IntegrationOperationsScript.Text(m, "message") : null,
                    IntegrationOperationsScript.Text(run, "correlationId"),
                    reference);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed record FailedRun(string? Step, string? ErrorCode, string? ErrorMessage, string? CorrelationId, RunRef? RunReference);

    private sealed record RunRef(string LogicApp, string Workflow, string RunId);
}
