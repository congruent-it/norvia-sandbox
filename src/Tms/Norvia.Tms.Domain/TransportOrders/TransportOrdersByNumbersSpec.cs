using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>Transport Orders by their numbers, for enriching a list built from another aggregate (P4).</summary>
public sealed class TransportOrdersByNumbersSpec : Specification<TransportOrder>
{
    public TransportOrdersByNumbersSpec(IReadOnlyCollection<TransportOrderNumber> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        Query.Where(order => numbers.Contains(order.Number!)).AsNoTracking();
    }
}
