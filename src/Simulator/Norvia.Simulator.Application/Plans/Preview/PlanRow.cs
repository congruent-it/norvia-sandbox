using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>One booking of a plan as the preview shows it: what is sent, never who receives it, and what the Planner means to do with its order.</summary>
public sealed record PlanRow(
    int Step,
    string Actor,
    string ShipperName,
    string Reference,
    string Route,
    int DayOffset,
    decimal WeightKg,
    BadBookingKind? Bad,
    ExpectedAnswer Expected,
    PlannerIntent? Planner = null,
    bool Reassign = false);
