namespace Norvia.Tms.Application.Fleet.Planning;

/// <param name="AvailableVehicles">How many Vehicles the Own fleet can use that day.</param>
/// <param name="Shifts">The Drivers who work that day, by name.</param>
public sealed record PlanningDayView(DateOnly Date, int AvailableVehicles, IReadOnlyList<ShiftView> Shifts);
