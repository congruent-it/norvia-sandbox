using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

public sealed class TransportOrderByIdSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderByIdSpec(TransportOrderId id)
    {
        Id = id;
        Query.Where(order => order.Id == id).AsNoTracking();
    }

    public TransportOrderId Id { get; }
}
