using Ardalis.Result;
using Mediator;

namespace Norvia.DriverApp.Application.Reports.StartTrip;

/// <summary>The Driver presses Start Trip on a Dispatched Trip (P12).</summary>
/// <param name="DriverId">The Driver ID from the Driver's token, never from the request (ADR 0005, ADR 0037).</param>
public sealed record StartTripCommand(string TripNumber, string DriverId) : ICommand<Result<StartTripResult>>;
