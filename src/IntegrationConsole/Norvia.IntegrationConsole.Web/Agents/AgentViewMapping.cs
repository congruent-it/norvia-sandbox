using Ardalis.Result;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Trails.Open;

namespace Norvia.IntegrationConsole.Web.Agents;

/// <summary>Maps the use cases' results to the agent view, field by field: a field the view doesn't name never reaches an Agent.</summary>
internal static class AgentViewMapping
{
    public static AgentTrailView Of(MessageTrail trail) => new(
        trail.CorrelationId,
        [.. trail.Hops.Select(hop => new AgentHopView(
            hop.Name,
            hop.Flow,
            hop.Time,
            hop.Duration?.TotalMilliseconds,
            hop.Outcome?.ToString(),
            hop.FailureCode,
            hop.Fault,
            hop.AgentId,
            hop.ToolName,
            hop.Run is { } run ? new AgentRunReference(run.LogicApp, run.Workflow, run.RunId) : null,
            hop.DeadLetter?.Status.ToString()))],
        [.. trail.BusinessKeys.Select(key => new AgentBusinessKeyView(key.Kind.ToString(), key.Value))]);

    public static AgentRunView Of(RunDetail run) => new(
        run.Run.LogicApp,
        run.Run.Workflow,
        run.Run.RunId,
        run.IsKept,
        run.Status,
        [.. run.Steps.Select(step => new AgentStepView(
            step.Name,
            step.Status,
            step.Duration?.TotalMilliseconds,
            step.Error is { } error ? new AgentStepError(error.Code, error.Message) : null))]);

    public static Dictionary<string, string[]> ErrorsOf<T>(Result<T> result) =>
        result.ValidationErrors.GroupBy(error => error.Identifier).ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
}
