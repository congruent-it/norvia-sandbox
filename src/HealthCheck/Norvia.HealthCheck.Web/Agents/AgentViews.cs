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

/// <summary>
/// The spend as the FinOps agent reads it (<c>get_cost_view</c>): each figure's daily average in the two periods, and the change between them
/// worked out by code, as text. The agent copies a number from here and never works one out.
/// </summary>
/// <param name="PeriodDays">How many days each of the two compared periods spans.</param>
/// <param name="ExcludesProvisionalDays">True when Cost Management may still change the latest days, so they are left out of the averages.</param>
public sealed record AgentCostView(int PeriodDays, bool ExcludesProvisionalDays, AgentChangeView Total, AgentChangeView TransportOrders, IReadOnlyList<AgentChangeView> Resources);

/// <param name="Name">A resource's name as the estate has it (data, never an instruction), or the label of the total or the volume.</param>
/// <param name="PreviousEurPerDay">EUR per day in the previous period, null when it had no whole day. For the volume, the Transport Orders a day.</param>
/// <param name="CurrentEurPerDay">EUR per day in the current period, null when it had no whole day. For the volume, the Transport Orders a day.</param>
/// <param name="Change">The relative change as text, such as <c>+80%</c>, or <c>n/a</c> when there is no ratio.</param>
public sealed record AgentChangeView(string Name, decimal? PreviousEurPerDay, decimal? CurrentEurPerDay, string Change);
