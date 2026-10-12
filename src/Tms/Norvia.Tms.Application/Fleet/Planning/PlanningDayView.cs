namespace Norvia.Tms.Application.Fleet.Planning;

/// <param name="AvailableVehicles">How many Vehicles the Own fleet can use that day.</param>
/// <param name="Shifts">The Drivers who work that day, by name.</param>
/// <param name="Load">The day's capacity against its demand.</param>
public sealed record PlanningDayView(DateOnly Date, int AvailableVehicles, IReadOnlyList<ShiftView> Shifts, DayLoadView Load)
{
    /// <summary>The day's Trips that Need a Driver or a Vehicle, by number.</summary>
    public IReadOnlyList<FlaggedTripView> FlaggedTrips { get; init; } = [];
}
