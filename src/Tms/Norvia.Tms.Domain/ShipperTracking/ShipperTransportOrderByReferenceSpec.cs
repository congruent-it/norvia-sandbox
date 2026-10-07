using Ardalis.Specification;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>
/// One Transport Order of one Shipper by the Shipper's own reference, which is unique per Shipper (P8). A Rejected booking has
/// no Transport Order number and is left out, as in the list.
/// </summary>
public sealed class ShipperTransportOrderByReferenceSpec : SingleResultSpecification<TransportOrder>
{
    public ShipperTransportOrderByReferenceSpec(ShipperId shipperId, string shipperReference)
    {
        ArgumentNullException.ThrowIfNull(shipperId);
        ArgumentException.ThrowIfNullOrWhiteSpace(shipperReference);
        Query.Where(order => order.ShipperId == shipperId && order.ShipperReference == shipperReference && order.Status != TransportOrderStatus.Rejected).AsNoTracking();
    }
}
