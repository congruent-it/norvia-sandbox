using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The Transport Orders waiting for Rating, oldest first, a batch at a time.</summary>
/// <param name="skip">How many of the oldest to pass over, such as those a sweep already tried and couldn't rate.</param>
public sealed class TransportOrdersNotYetRatedSpec : Specification<TransportOrder>
{
    public TransportOrdersNotYetRatedSpec(int skip, int batchSize) =>
        Query.Where(order => order.RatingResult == RatingResult.NotYetRated)
            .OrderBy(order => order.ReceivedAt)
            .ThenBy(order => order.Id)
            .Skip(skip)
            .Take(batchSize)
            .AsNoTracking();
}
