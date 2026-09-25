using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Rejected booking with this ID; an Accepted Transport Order's ID finds nothing.</summary>
public sealed class RejectedBookingByIdSpec : SingleResultSpecification<TransportOrder>
{
    public RejectedBookingByIdSpec(TransportOrderId id)
    {
        Id = id;
        Query.Where(order => order.Id == id && order.Status == TransportOrderStatus.Rejected).AsNoTracking();
    }

    public TransportOrderId Id { get; }
}
