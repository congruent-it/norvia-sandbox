using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Transport Order with this number, tracked, so checking whether it is Billable (P9) can change it.</summary>
public sealed class TransportOrderToBillSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderToBillSpec(TransportOrderNumber number)
    {
        Number = number;
        Query.Where(order => order.Number == number);
    }

    public TransportOrderNumber Number { get; }
}
