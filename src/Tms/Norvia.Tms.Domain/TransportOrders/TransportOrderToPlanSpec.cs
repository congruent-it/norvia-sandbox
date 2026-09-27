using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Transport Order with this number, tracked, so the Planner's assignment can change it.</summary>
public sealed class TransportOrderToPlanSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderToPlanSpec(TransportOrderNumber number)
    {
        Number = number;
        Query.Where(order => order.Number == number);
    }

    public TransportOrderNumber Number { get; }
}
