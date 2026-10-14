namespace Norvia.Simulator.Domain.Log;

/// <summary>Which stage of the funnel an outcome belongs to: a booking sent to the Partner API, or a call the Planner made to the TMS.</summary>
public static class LogOutcomes
{
    public static IReadOnlyList<LogOutcome> Booking { get; } =
        [LogOutcome.Accepted, LogOutcome.AlreadyBooked, LogOutcome.Rejected, LogOutcome.Refused, LogOutcome.NotReceived];

    public static IReadOnlyList<LogOutcome> Planning { get; } =
        [LogOutcome.Planned, LogOutcome.Reassigned, LogOutcome.PlanningRefused, LogOutcome.PlanningNotReceived];
}
