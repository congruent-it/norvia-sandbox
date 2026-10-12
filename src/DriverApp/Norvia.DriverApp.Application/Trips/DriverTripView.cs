namespace Norvia.DriverApp.Application.Trips;

/// <summary>The latest version of a Trip, as its Driver sees it.</summary>
/// <param name="Status">The Trip's status in the Driver app. The Driver app holds only Dispatched Trips until the Driver can start one (P12).</param>
public sealed record DriverTripView(
    string TripNumber,
    int Version,
    string Status,
    string DriverId,
    string DriverName,
    DateOnly Day,
    TimeOnly PlannedStart,
    TimeSpan PlannedDrivingDuration,
    TripVehicleDto Vehicle,
    IReadOnlyList<StopDto> Stops);
