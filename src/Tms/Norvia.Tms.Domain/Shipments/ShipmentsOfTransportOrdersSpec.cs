using Ardalis.Specification;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipments of several Transport Orders, for enriching a list built from them (P8).</summary>
public sealed class ShipmentsOfTransportOrdersSpec : Specification<Shipment>
{
    public ShipmentsOfTransportOrdersSpec(IReadOnlyCollection<TransportOrderId> transportOrderIds)
    {
        ArgumentNullException.ThrowIfNull(transportOrderIds);
        Query.Where(shipment => transportOrderIds.Contains(shipment.TransportOrderId)).AsNoTracking();
    }
}
