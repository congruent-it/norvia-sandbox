using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>Transport Orders by their IDs, for enriching a list built from Shipments (P10).</summary>
public sealed class TransportOrdersByIdsSpec : Specification<TransportOrder>
{
    public TransportOrdersByIdsSpec(IReadOnlyCollection<TransportOrderId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Query.Where(order => ids.Contains(order.Id)).AsNoTracking();
    }
}
