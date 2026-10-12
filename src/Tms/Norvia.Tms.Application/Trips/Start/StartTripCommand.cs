using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.Start;

/// <summary>
/// The Driver starts a Dispatched Trip on its day (P12), reported by the Driver reports Logic App.
/// </summary>
/// <param name="DriverId">The Driver ID from the Driver's token, never from the Driver app's payload (ADR 0005).</param>
/// <param name="CorrelationId">The Message trail of the Driver's action, which the Logic App continues.</param>
public sealed record StartTripCommand(string Number, string DriverId, string CorrelationId) : ICommand<Result<StartTripResult>>;
