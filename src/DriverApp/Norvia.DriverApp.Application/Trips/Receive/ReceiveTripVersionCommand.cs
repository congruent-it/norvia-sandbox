using Ardalis.Result;
using Mediator;

namespace Norvia.DriverApp.Application.Trips.Receive;

/// <summary>A Trip version, as the Trip dispatch Logic App pushes it (the Canonical Model's <c>TripDispatched</c>).</summary>
/// <param name="PlannedDrivingDuration">An ISO 8601 duration, such as <c>PT6H</c>.</param>
public sealed record ReceiveTripVersionCommand(
    string TripNumber,
    int Version,
    string CorrelationId,
    DateOnly Day,
    TimeOnly PlannedStart,
    string PlannedDrivingDuration,
    TripVehicleDto Vehicle,
    string DriverId,
    string DriverName,
    IReadOnlyList<StopDto> Stops) : ICommand<Result<TripVersionOutcome>>;
