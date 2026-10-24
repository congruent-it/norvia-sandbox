namespace Norvia.IntegrationConsole.Web.Agents;

// The agent view is a narrower read of the console's use cases (ADR 0040): statuses, codes, times, numbers and business names.
// Every field here is on an allowlist, so a field added to a use case's result stays out of the view until someone adds it
// here. No message body, no step input or output, no actor who handled a dead-lettered message: AgentViewsTests holds the list.

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

/// <summary>The failed runs of the last 24 hours, newest first (<c>list_failed_runs</c>).</summary>
public sealed record AgentFailedRunsView(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<AgentFailedRunView> Runs);

/// <param name="Run">The Logic App run, which <c>get_run</c> opens; null where there are no Logic Apps, as locally.</param>
/// <param name="CorrelationId">The Message trail the run tracked, which <c>get_message_trail</c> opens, or null.</param>
public sealed record AgentFailedRunView(string Flow, AgentRunReference? Run, DateTimeOffset Start, string? CorrelationId, string? FailedStep, AgentStepError? Error);

/// <summary>Every queue and subscription with its dead-letter count and the age of its oldest message (<c>list_dead_letter_queues</c>).</summary>
public sealed record AgentDeadLetterQueuesView(DateTimeOffset At, IReadOnlyList<AgentDeadLetterQueueView> Queues);

/// <param name="Name">A queue's name, or a subscription's as <c>topic/subscription</c>, which <c>peek_dead_letters</c> takes.</param>
public sealed record AgentDeadLetterQueueView(string Name, long Count, double? OldestAgeSeconds);

/// <summary>The oldest dead-lettered messages of one queue or subscription, peeked (<c>peek_dead_letters</c>).</summary>
public sealed record AgentDeadLettersView(string Entity, IReadOnlyList<AgentDeadLetterView> Messages, bool HasMore);

/// <param name="ApplicationProperties">The application properties as text, each value cut short. Never the body.</param>
public sealed record AgentDeadLetterView(
    long SequenceNumber,
    DateTimeOffset EnqueuedAt,
    string? Reason,
    string? Description,
    int DeliveryCount,
    string? CorrelationId,
    IReadOnlyList<AgentBusinessKeyView> BusinessKeys,
    IReadOnlyDictionary<string, string> ApplicationProperties);
