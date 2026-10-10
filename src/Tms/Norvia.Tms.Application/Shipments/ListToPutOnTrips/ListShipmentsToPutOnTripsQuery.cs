using Mediator;

namespace Norvia.Tms.Application.Shipments.ListToPutOnTrips;

/// <summary>
/// The Released Own fleet Shipments not on a Trip yet (P11), by collection time. A day keeps those whose collection day it is.
/// </summary>
public sealed record ListShipmentsToPutOnTripsQuery(DateOnly? Day = null) : IQuery<IReadOnlyList<ShipmentToPutOnTripSummary>>;
