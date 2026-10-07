using Ardalis.Specification;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>
/// The Transport Orders of one Shipper that a Shipper can follow (P8). A Rejected booking has no Transport Order number and
/// is left out, and so is every other Shipper's.
/// </summary>
public sealed class ShipperTransportOrdersSpec : Specification<TransportOrder>
{
    public ShipperTransportOrdersSpec(ShipperId shipperId)
    {
        ArgumentNullException.ThrowIfNull(shipperId);
        Query.Where(order => order.ShipperId == shipperId && order.Status != TransportOrderStatus.Rejected).AsNoTracking();
    }
}
