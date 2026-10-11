using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.TakeShipmentOff;

/// <summary>
/// The Planner takes a Shipment off its Trip until it is Collected (P11): its two Stops leave the Trip and the Shipment goes back to Planned
/// in the same transaction, to be re-planned in P3. <c>ShipmentTakenOffTrip</c> is published under <c>CorrelationId</c>; on a Dispatched Trip
/// (also one that is In progress) this is an Amend as well, which raises the next version under the same correlation ID. A Collected Shipment is
/// refused with ShipmentCollected, one that is not on the Trip with ShipmentNotOnTrip, and a refusal changes nothing.
/// </summary>
/// <param name="Reason">Why the Planner takes the Shipment off, in their words.</param>
public sealed record TakeShipmentOffTripCommand(string Number, string ShipmentNumber, string Reason, string CorrelationId) : ICommand<Result<TakeShipmentOffTripResult>>;
