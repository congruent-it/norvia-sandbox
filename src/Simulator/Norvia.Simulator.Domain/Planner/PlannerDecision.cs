namespace Norvia.Simulator.Domain.Planner;

/// <summary>Who the Planner assigns an order to, whether it took room on the Own fleet's day for it, and why, as the log words it.</summary>
public sealed record PlannerDecision(Assignee Assignee, bool ReservedOwnFleet, string Reason);
