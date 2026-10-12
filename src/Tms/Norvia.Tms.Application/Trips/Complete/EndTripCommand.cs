using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.Complete;

/// <summary>
/// The Driver ends an In progress Trip once every Stop has an outcome (P12), reported by the Driver reports Logic App.
/// </summary>
/// <param name="DriverId">The Driver ID from the Driver's token, never from the Driver app's payload (ADR 0005).</param>
/// <param name="CorrelationId">The Message trail of the Driver's action, which the Logic App continues.</param>
public sealed record EndTripCommand(string Number, string DriverId, string CorrelationId) : ICommand<Result<EndTripResult>>;
