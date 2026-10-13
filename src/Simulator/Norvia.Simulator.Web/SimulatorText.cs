using Norvia.Simulator.Domain.Log;
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

    public static string Of(RunMode mode, TimeSpan window) =>
        mode == RunMode.Burst ? "Burst" : $"Paced over {(int)window.TotalMinutes} min";

    /// <summary>A time of day in Norvia's own time zone, which the people watching a run keep.</summary>
    public static string Time(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    public static string DateAndTime(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone).ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
