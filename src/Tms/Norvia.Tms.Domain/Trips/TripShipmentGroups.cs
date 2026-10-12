using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Shipments of a Trip the Driver ended (P12), by what became of them.</summary>
/// <param name="Delivered">The Shipments whose delivery Stop is Delivered.</param>
/// <param name="NotDelivered">The Shipments collected but not delivered, which are Awaiting re-delivery now.</param>
/// <param name="NotCollected">The Shipments whose collection failed, which are Planned again.</param>
public sealed record TripShipmentGroups(
    IReadOnlyList<ShipmentNumber> Delivered,
    IReadOnlyList<ShipmentNumber> NotDelivered,
    IReadOnlyList<ShipmentNumber> NotCollected);
