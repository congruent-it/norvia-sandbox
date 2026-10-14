namespace Norvia.Simulator.Domain.Planner;

/// <summary>Where the Planner means to put an order: Own fleet when the day's capacity has room for it, otherwise a Carrier.</summary>
public enum PlannerIntent
{
    OwnFleetIfItFits,
    Carrier,
}
