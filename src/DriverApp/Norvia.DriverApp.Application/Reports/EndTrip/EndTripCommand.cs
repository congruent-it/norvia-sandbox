using Ardalis.Result;
using Mediator;

namespace Norvia.DriverApp.Application.Reports.EndTrip;

/// <summary>The Driver presses End Trip on an In progress Trip whose Stops all have an outcome (P12).</summary>
/// <param name="DriverId">The Driver ID from the Driver's token, never from the request (ADR 0005, ADR 0037).</param>
public sealed record EndTripCommand(string TripNumber, string DriverId) : ICommand<Result<EndTripResult>>;
