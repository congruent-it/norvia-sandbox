namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>A Logic App run a hop was: the Logic App Standard app, its workflow, and the run's ID.</summary>
public sealed record WorkflowRun(string LogicApp, string Workflow, string RunId)
{
    /// <summary>The run an operator opened by its parts, trimmed, or null when a part is missing.</summary>
    internal static WorkflowRun? Of(string? logicApp, string? workflow, string? runId) =>
        logicApp?.Trim() is { Length: > 0 } app && workflow?.Trim() is { Length: > 0 } flow && runId?.Trim() is { Length: > 0 } id
            ? new WorkflowRun(app, flow, id)
            : null;
}
