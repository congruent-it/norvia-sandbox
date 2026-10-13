namespace Norvia.DriverApp.Application.Trips.GetStop;

/// <summary>A Stop with the Trip it is on, as the Stop screen shows it.</summary>
/// <param name="Status">The Trip's status in the Driver app: Dispatched until the TMS has recorded the Driver's Start Trip, then In progress.</param>
public sealed record DriverStopView(string TripNumber, int Version, string Status, DateOnly Day, StopView Stop);
