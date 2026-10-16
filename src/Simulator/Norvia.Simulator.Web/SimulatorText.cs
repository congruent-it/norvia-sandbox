using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web;

/// <summary>How the Simulator words its outcomes, statuses and the ways a booking is bad.</summary>
public static class SimulatorText
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static string Of(LogOutcome outcome) => outcome switch
    {
        LogOutcome.Info => "Info",
        LogOutcome.Accepted => "Accepted",
        LogOutcome.AlreadyBooked => "Already booked",
        LogOutcome.Rejected => "Rejected",
        LogOutcome.Refused => "Refused",
        LogOutcome.NotReceived => "Not received",
        LogOutcome.Planned => "Planned",
        LogOutcome.Reassigned => "Reassigned",
        LogOutcome.PlanningRefused => "Planning refused",
        LogOutcome.PlanningNotReceived => "Planning not received",
        LogOutcome.Released => "Released",
        LogOutcome.BookingCancelled => "Booking cancelled",
        LogOutcome.ExceptionResolved => "Exception resolved",
        LogOutcome.ExceptionFound => "Exception found",
        LogOutcome.Confirmed => "Confirmed",
        LogOutcome.Declined => "Declined",
        LogOutcome.NoAnswer => "No answer",
        LogOutcome.TrackingSent => "Tracking sent",
        LogOutcome.ProofOfDeliverySent => "Proof of delivery sent",
        LogOutcome.CarrierRefused => "Carrier refused",
        LogOutcome.CarrierNotReceived => "Carrier not received",
        LogOutcome.TripBuilt => "Trip built",
        LogOutcome.TripDispatched => "Trip dispatched",
        LogOutcome.DriverReported => "Driver reported",
        LogOutcome.DriverRefused => "Driver app refused",
        LogOutcome.DriverNotReceived => "Driver report not received",
        LogOutcome.TripCompleted => "Trip completed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Every outcome has a wording."),
    };

    public static string Of(SimulationRunStatus status) => status switch
    {
        SimulationRunStatus.Running => "Running",
        SimulationRunStatus.Completed => "Completed",
        SimulationRunStatus.Cancelled => "Cancelled",
        SimulationRunStatus.Failed => "Failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Every status has a wording."),
    };

    public static string Of(BadBookingKind kind) => kind switch
    {
        BadBookingKind.NoLane => "No Lane",
        BadBookingKind.RepeatedReference => "Repeated reference",
        BadBookingKind.ConflictingReference => "Conflicting reference",
        BadBookingKind.InvalidContent => "Invalid content",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Every kind has a wording."),
    };

    public static string Of(ExpectedAnswer answer) => answer switch
    {
        ExpectedAnswer.Accepted => "Accepted",
        ExpectedAnswer.AlreadyBooked => "Already booked",
        ExpectedAnswer.Rejected => "Rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, "Every answer has a wording."),
    };

    /// <summary>What the Planner means to do with a booking's order, for the preview: nothing when it isn't assigned.</summary>
    public static string Of(PlannerIntent? intent, bool reassign)
    {
        var meant = intent switch
        {
            null => "Not assigned",
            PlannerIntent.OwnFleetIfItFits => "Own fleet if it fits, else a Carrier",
            PlannerIntent.Carrier => "A Carrier",
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Every intent has a wording."),
        };
        return reassign ? $"{meant}, then reassigned" : meant;
    }

    public static string Of(RunMode mode, TimeSpan window) =>
        mode == RunMode.Burst ? "Burst" : $"Paced over {(int)window.TotalMinutes} min";

    /// <summary>A time of day in Norvia's own time zone, which the people watching a run keep.</summary>
    public static string Time(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    public static string DateAndTime(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone).ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
