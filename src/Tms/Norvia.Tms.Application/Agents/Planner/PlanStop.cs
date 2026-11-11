namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>A run that stopped before the end of the list; <paramref name="OrdersLeft"/> orders, from <paramref name="NextOrder"/> on, have no Suggestion.</summary>
public sealed record PlanStop(PlanStopReason Reason, int OrdersLeft, string NextOrder);
