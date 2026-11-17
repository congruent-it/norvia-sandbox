using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Transport Order with this number, tracked, so the Planner can replace its tracking code (ADR 0052).</summary>
public sealed class TransportOrderToReplaceTrackingCodeSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderToReplaceTrackingCodeSpec(TransportOrderNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);
        Query.Where(order => order.Number == number);
    }
}
