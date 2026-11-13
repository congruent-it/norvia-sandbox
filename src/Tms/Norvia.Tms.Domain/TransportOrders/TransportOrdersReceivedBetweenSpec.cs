using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The times Norvia took in Transport Orders during a window, <see cref="From"/> included and <see cref="Until"/> not. A Rejected booking is not a Transport Order, so it is not in it.</summary>
public sealed class TransportOrdersReceivedBetweenSpec : Specification<TransportOrder, DateTimeOffset>
{
    public TransportOrdersReceivedBetweenSpec(DateTimeOffset from, DateTimeOffset until)
    {
        From = from;
        Until = until;
        Query.Where(order => order.Status != TransportOrderStatus.Rejected && order.ReceivedAt >= from && order.ReceivedAt < until)
            .OrderBy(order => order.ReceivedAt)
            .Select(order => order.ReceivedAt);
    }

    public DateTimeOffset From { get; }

    public DateTimeOffset Until { get; }
}
