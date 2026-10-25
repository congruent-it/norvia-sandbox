namespace Norvia.HealthCheck.Web.Agents;

// The agent view is a narrower read of the app's use cases (ADR 0040): every field here is on an allowlist, so a field added to a use
// case's result stays out of the view until someone adds it here. Evidence has already been masked by the checklist; the view carries
// nothing else about a resource.

/// <summary>The Findings of a health check as an Agent reads them (<c>run_checks</c>). An empty list means every Check passed.</summary>
/// <param name="Estate">What the facts describe, such as <c>as-found</c>.</param>
/// <param name="ChecksRun">How many Checks ran.</param>
public sealed record AgentFindingsView(string Estate, int ChecksRun, IReadOnlyList<AgentFindingView> Findings);

/// <param name="Severity">The Check's own: Low, Medium, High or Critical.</param>
/// <param name="AsFoundToggle">The as-found toggle that makes the estate this way.</param>
/// <param name="Fix">The Bicep parameter or setting that puts it right, the Check's own.</param>
public sealed record AgentFindingView(
    string Id,
    string CheckId,
    string Title,
    string Rule,
    string Severity,
    IReadOnlyList<AgentEvidenceView> Evidence,
    string AsFoundToggle,
    string Fix);

public sealed record AgentEvidenceView(string Subject, string Detail);

/// <summary>The Logic Apps that failed lately (<c>list_failed_runs</c>), each with whether an alert would have told anyone.</summary>
/// <param name="WindowDays">How many days back the failed runs are counted.</param>
public sealed record AgentFailedRunsView(string Estate, int WindowDays, IReadOnlyList<AgentFailedFlowView> Flows);

public sealed record AgentFailedFlowView(string Flow, int FailedRuns, bool HasFailedRunAlert);

/// <summary>The queues and subscriptions that hold dead-lettered messages (<c>list_dead_letter_queues</c>), each with whether an alert watches it.</summary>
public sealed record AgentDeadLetterQueuesView(string Estate, IReadOnlyList<AgentDeadLetterQueueView> Queues);

public sealed record AgentDeadLetterQueueView(string Name, int DeadLetteredMessages, bool HasDeadLetterAlert);
