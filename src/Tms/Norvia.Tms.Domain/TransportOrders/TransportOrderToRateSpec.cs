using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Transport Order with this number, tracked, so Rating can change it.</summary>
public sealed class TransportOrderToRateSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderToRateSpec(TransportOrderNumber number)
    {
        Number = number;
        Query.Where(order => order.Number == number);
    }

    public TransportOrderNumber Number { get; }
}
