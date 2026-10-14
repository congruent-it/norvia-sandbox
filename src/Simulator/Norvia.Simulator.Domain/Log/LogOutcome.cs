namespace Norvia.Simulator.Domain.Log;

public enum LogOutcome
{
    /// <summary>A line of the Simulator itself: the run started, was cancelled or finished.</summary>
    Info,

    Accepted,
    AlreadyBooked,
    Rejected,

    /// <summary>Norvia refused the Persona's credentials.</summary>
    Refused,

    /// <summary>Nothing reached Norvia, or its answer couldn't be read.</summary>
    NotReceived,

    /// <summary>The Planner assigned a Transport Order, which planned its Shipment.</summary>
    Planned,

    /// <summary>The Planner reassigned a Planned Shipment.</summary>
    Reassigned,

    /// <summary>The TMS refused the Planner's assignment or reassignment, with a code that says which rule it broke.</summary>
    PlanningRefused,

    /// <summary>The Planner's call didn't reach the TMS, or its answer couldn't be read.</summary>
    PlanningNotReceived,
}
