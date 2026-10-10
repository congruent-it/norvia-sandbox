using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.Dispatch;

/// <summary>
/// The Planner dispatches a Draft Trip (P11). The TMS re-checks every Trip rule, then, in one transaction, sets the Trip and its Shipments to
/// Dispatched and publishes <c>TripDispatched</c> version 1 with the whole Trip. The Dispatch starts a Message trail of its own.
/// </summary>
/// <param name="CorrelationId">The new Message trail the TMS starts for this Dispatch, as it starts in the TMS UI.</param>
public sealed record DispatchTripCommand(string Number, string CorrelationId) : ICommand<Result<DispatchTripResult>>;
