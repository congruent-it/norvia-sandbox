namespace Norvia.IntegrationConsole.Web.Agents;

// The agent view is a narrower read of the console's use cases (ADR 0040): statuses, codes, times, numbers and business names.
// Every field here is on an allowlist, so a field added to a use case's result stays out of the view until someone adds it
// here. No message body, no step input or output, no dead-letter reason or actor: AgentViewTests holds the list.

/// <summary>A Message trail as an Agent reads it.</summary>
public sealed record AgentTrailView(string CorrelationId, IReadOnlyList<AgentHopView> Hops, IReadOnlyList<AgentBusinessKeyView> BusinessKeys);

/// <param name="DurationMs">How long it took, or null when the telemetry recorded only a moment.</param>
/// <param name="DeadLetter">What became of a dead-lettered hop's message: OnQueue, Resubmitted or Discarded.</param>
public sealed record AgentHopView(
    string Name,
    string? Flow,
    DateTimeOffset Time,
    double? DurationMs,
    string? Outcome,
    string? FailureCode,
    string? Fault,
    string? AgentId,
    string? ToolName,
    AgentRunReference? Run,
    string? DeadLetter);

/// <summary>The Logic App run a hop was, which <c>get_run</c> opens.</summary>
public sealed record AgentRunReference(string LogicApp, string Workflow, string RunId);

public sealed record AgentBusinessKeyView(string Kind, string Value);

/// <summary>A Logic App run as an Agent reads it: its status and its steps' status, duration and error.</summary>
public sealed record AgentRunView(string LogicApp, string Workflow, string RunId, bool IsKept, string? Status, IReadOnlyList<AgentStepView> Steps);

public sealed record AgentStepView(string Name, string Status, double? DurationMs, AgentStepError? Error);

public sealed record AgentStepError(string Code, string? Message);
