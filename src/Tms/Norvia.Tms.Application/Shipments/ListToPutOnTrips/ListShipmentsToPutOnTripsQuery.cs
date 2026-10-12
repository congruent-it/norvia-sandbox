using Mediator;

namespace Norvia.Tms.Application.Shipments.ListToPutOnTrips;

/// <summary>
/// The Released Own fleet Shipments not on a Trip yet (P11), and those Awaiting re-delivery (P12), by collection time. A day keeps those whose collection day it is, and every Re-delivery, which collects no more.
/// </summary>
public sealed record ListShipmentsToPutOnTripsQuery(DateOnly? Day = null) : IQuery<IReadOnlyList<ShipmentToPutOnTripSummary>>;
