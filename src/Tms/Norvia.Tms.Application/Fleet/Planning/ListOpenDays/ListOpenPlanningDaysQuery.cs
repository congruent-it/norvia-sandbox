using Mediator;

namespace Norvia.Tms.Application.Fleet.Planning.ListOpenDays;

/// <summary>The open days of the Planning horizon, in order, for the Planner's Capacity view: one row per day.</summary>
public sealed record ListOpenPlanningDaysQuery : IQuery<IReadOnlyList<PlanningDayView>>;
