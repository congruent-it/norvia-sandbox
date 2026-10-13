using Norvia.DriverApp.Application.Reports;

namespace Norvia.DriverApp.Application.Trips;

/// <summary>The latest version of a Trip, as its Driver sees it.</summary>
/// <param name="Status">The Trip's status in the Driver app: Dispatched until the TMS has recorded the Driver's Start Trip, then In progress (P12).</param>
/// <param name="CanStart">Whether to offer Start Trip: the Trip is Dispatched, it is its day in Norvia's time, and no Start Trip is waiting for an answer.</param>
/// <param name="Stops">The Trip's Stops in the Planner's sequence, each with what the Driver reported on it, then the completed Stops whose Shipment a later version took off.</param>
/// <param name="Reports">The Driver's reports on the Trip, oldest first, each with its state.</param>
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
    IReadOnlyList<StopView> Stops,
    bool CanStart,
    IReadOnlyList<TripReportView> Reports);
