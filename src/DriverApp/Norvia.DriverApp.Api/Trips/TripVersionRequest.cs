using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.Receive;

namespace Norvia.DriverApp.Api.Trips;

/// <summary>A Trip version in the Canonical Model's <c>TripDispatched</c> shape, as the Trip dispatch Logic App sends it. The event's own envelope (its ID, type and time) is not the Driver app's concern.</summary>
public sealed record TripVersionRequest(
    string TripNumber,
    int Version,
    string CorrelationId,
    DateOnly Day,
    TimeOnly PlannedStart,
    string PlannedDrivingDuration,
    TripVehicleDto Vehicle,
    TripDriverRequest Driver,
    IReadOnlyList<StopDto> Stops)
{
    public ReceiveTripVersionCommand ToCommand() =>
        new(TripNumber, Version, CorrelationId, Day, PlannedStart, PlannedDrivingDuration, Vehicle, Driver.DriverId, Driver.Name, Stops);
}
