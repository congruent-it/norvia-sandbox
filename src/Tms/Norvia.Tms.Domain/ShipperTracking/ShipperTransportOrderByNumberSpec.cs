using Ardalis.Specification;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>One Transport Order of one Shipper by Norvia's number (P8). Another Shipper's matches nothing.</summary>
public sealed class ShipperTransportOrderByNumberSpec : SingleResultSpecification<TransportOrder>
{
    public ShipperTransportOrderByNumberSpec(ShipperId shipperId, TransportOrderNumber number)
    {
        ArgumentNullException.ThrowIfNull(shipperId);
        ArgumentNullException.ThrowIfNull(number);
        Query.Where(order => order.ShipperId == shipperId && order.Number == number).AsNoTracking();
    }
}
