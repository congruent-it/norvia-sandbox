namespace Norvia.DriverApp.Application.Trips;

/// <summary>The latest version of a Trip, as its Driver sees it.</summary>
public sealed record DriverTripView(
    string TripNumber,
    int Version,
    string DriverId,
    string DriverName,
    DateOnly Day,
    TimeOnly PlannedStart,
    TimeSpan PlannedDrivingDuration,
    TripVehicleDto Vehicle,
    IReadOnlyList<StopDto> Stops);
