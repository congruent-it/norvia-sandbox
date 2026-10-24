using Ardalis.Result;
using Norvia.IntegrationConsole.Application.DeadLetters.List;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Runs.Failed;
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

    public static AgentFailedRunsView Of(FailedRunList list) => new(
        list.From,
        list.To,
        [.. list.Runs.Select(run => new AgentFailedRunView(
            run.Flow,
            run.Run is { } reference ? new AgentRunReference(reference.LogicApp, reference.Workflow, reference.RunId) : null,
            run.Start,
            run.CorrelationId,
            run.FailedStep,
            run.Error is { } error ? new AgentStepError(error.Code, error.Message) : null))]);

    public static AgentDeadLetterQueuesView Of(DeadLetterQueueList list) => new(
        list.At,
        [.. list.Queues.Select(queue => new AgentDeadLetterQueueView(queue.Name, queue.Count, queue.OldestAge?.TotalSeconds))]);

    /// <summary>At most <paramref name="limit"/> messages, the oldest first: a tool result stays small enough to read.</summary>
    public static AgentDeadLettersView Of(DeadLetteredMessageList list, int limit) => new(
        list.Entity,
        [.. list.Messages.Take(limit).Select(message => new AgentDeadLetterView(
            message.SequenceNumber,
            message.EnqueuedAt,
            message.Reason,
            message.Description,
            message.DeliveryCount,
            message.CorrelationId,
            [.. message.BusinessKeys.Select(key => new AgentBusinessKeyView(key.Kind.ToString(), key.Value))],
            message.ApplicationProperties ?? new Dictionary<string, string>()))],
        list.HasMore || list.Messages.Count > limit);

    public static Dictionary<string, string[]> ErrorsOf<T>(Result<T> result) =>
        result.ValidationErrors.GroupBy(error => error.Identifier).ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
}
