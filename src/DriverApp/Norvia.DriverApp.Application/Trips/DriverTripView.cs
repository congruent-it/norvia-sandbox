using Norvia.DriverApp.Domain.Trips;

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
    TripVehicle Vehicle,
    IReadOnlyList<TripStop> Stops);
