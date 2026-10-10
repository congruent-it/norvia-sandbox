using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Trips;

/// <summary>Why a new order of a Trip's Stops can't be taken: a <see cref="TripRefusalCode"/> and, when it is one Shipment's fault, which.</summary>
/// <param name="Shipment">The Shipment whose delivery comes before its collection; null when the order isn't the Trip's Stops.</param>
public sealed record StopOrderFault(TripRefusalCode Code, ShipmentId? Shipment = null);
