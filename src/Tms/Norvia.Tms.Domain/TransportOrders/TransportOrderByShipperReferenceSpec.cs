using Ardalis.Specification;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Accepted Transport Order that holds a Shipper reference for its Shipper, if any.</summary>
public sealed class TransportOrderByShipperReferenceSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderByShipperReferenceSpec(ShipperId shipperId, string shipperReference)
    {
        ShipperId = shipperId;
        ShipperReference = shipperReference;
        Query.Where(order => order.ShipperId == shipperId && order.ShipperReference == shipperReference).AsNoTracking();
    }

    public ShipperId ShipperId { get; }

    public string ShipperReference { get; }
}
