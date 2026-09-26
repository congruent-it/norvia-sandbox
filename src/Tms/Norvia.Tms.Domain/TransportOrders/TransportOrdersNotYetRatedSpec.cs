using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Transport Orders waiting for Rating, oldest first, a batch at a time.</summary>
public sealed class TransportOrdersNotYetRatedSpec : Specification<TransportOrder>
{
    public TransportOrdersNotYetRatedSpec(int batchSize) =>
        Query.Where(order => order.RatingResult == RatingResult.NotYetRated)
            .OrderBy(order => order.ReceivedAt)
            .Take(batchSize)
            .AsNoTracking();
}
