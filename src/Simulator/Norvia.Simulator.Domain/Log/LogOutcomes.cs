namespace Norvia.Simulator.Domain.Log;

/// <summary>Which stage of the funnel an outcome belongs to: a booking sent to the Partner API, a call the Planner made to the TMS, a call a Carrier made to the Partner API, or a call a Driver made to the Driver app.</summary>
public static class LogOutcomes
{
    public static IReadOnlyList<LogOutcome> Booking { get; } =
        [LogOutcome.Accepted, LogOutcome.AlreadyBooked, LogOutcome.Rejected, LogOutcome.Refused, LogOutcome.NotReceived];

    public static IReadOnlyList<LogOutcome> Planning { get; } =
    [
        LogOutcome.Planned, LogOutcome.Reassigned, LogOutcome.Released, LogOutcome.BookingCancelled, LogOutcome.ExceptionFound, LogOutcome.ExceptionResolved,
        LogOutcome.TripBuilt, LogOutcome.TripDispatched, LogOutcome.PlanningRefused, LogOutcome.PlanningNotReceived,
    ];

    public static IReadOnlyList<LogOutcome> Carrier { get; } =
    [
        LogOutcome.Confirmed, LogOutcome.Declined, LogOutcome.NoAnswer, LogOutcome.TrackingSent, LogOutcome.ProofOfDeliverySent,
        LogOutcome.CarrierRefused, LogOutcome.CarrierNotReceived,
    ];

    public static IReadOnlyList<LogOutcome> Driver { get; } =
        [LogOutcome.DriverReported, LogOutcome.DriverRefused, LogOutcome.DriverNotReceived, LogOutcome.TripCompleted];
}
