using Ardalis.Specification;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>The one Transport Order a tracking code opens (ADR 0052). A replaced or unknown code matches nothing.</summary>
public sealed class TransportOrderByTrackingCodeSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderByTrackingCodeSpec(TrackingCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        Query.Where(order => order.TrackingCode == code).AsNoTracking();
    }
}
