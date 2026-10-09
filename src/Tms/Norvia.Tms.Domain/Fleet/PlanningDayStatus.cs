namespace Norvia.Tms.Domain.Fleet;

public enum PlanningDayStatus
{
    /// <summary>In the Planning horizon: the Planner can still change it.</summary>
    Open,

    /// <summary>Before today: read-only.</summary>
    Closed,
}
