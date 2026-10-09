namespace Norvia.Tms.Web.Capacity;

/// <summary>What the Planner enters to record an Absence or take a Vehicle off the road: who, the days from first to last, why.</summary>
public sealed record PeriodEntry(string SubjectId, DateOnly From, DateOnly To, string Reason, string Remark);
