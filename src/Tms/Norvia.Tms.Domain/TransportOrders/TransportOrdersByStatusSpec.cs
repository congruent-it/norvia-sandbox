using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

public sealed class TransportOrdersByStatusSpec : Specification<TransportOrder>
{
    public TransportOrdersByStatusSpec(TransportOrderStatus status)
    {
        Status = status;
        Query.Where(order => order.Status == status)
            .OrderBy(order => order.ReceivedAt)
            .AsNoTracking();
    }

    public TransportOrderStatus Status { get; }
}
