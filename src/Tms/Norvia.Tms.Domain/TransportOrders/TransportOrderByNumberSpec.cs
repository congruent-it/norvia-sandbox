using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

public sealed class TransportOrderByNumberSpec : SingleResultSpecification<TransportOrder>
{
    public TransportOrderByNumberSpec(TransportOrderNumber number)
    {
        Number = number;
        Query.Where(order => order.Number == number).AsNoTracking();
    }

    public TransportOrderNumber Number { get; }
}
