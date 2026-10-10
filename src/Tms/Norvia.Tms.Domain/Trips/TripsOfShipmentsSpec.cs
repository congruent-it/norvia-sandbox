using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Trips that carry any of these Shipments: a Shipment is on at most one Trip.</summary>
public sealed class TripsOfShipmentsSpec : Specification<Trip>
{
    public TripsOfShipmentsSpec(IReadOnlyCollection<ShipmentId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Query.Where(trip => trip.Stops.Any(stop => ids.Contains(stop.ShipmentId))).AsNoTracking();
    }
}
