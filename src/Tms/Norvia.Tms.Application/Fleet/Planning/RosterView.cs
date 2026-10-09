namespace Norvia.Tms.Application.Fleet.Planning;

/// <param name="Weekdays">The weekdays the Driver works, Monday first, with their Shift times.</param>
public sealed record RosterView(string DriverId, string DriverName, IReadOnlyList<RosterWeekdayView> Weekdays);
