using Ardalis.Specification;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>The rating exceptions: the Transport Orders whose Rating failed, oldest first.</summary>
public sealed class TransportOrdersRatingFailedSpec : Specification<TransportOrder>
{
    public TransportOrdersRatingFailedSpec() =>
        Query.Where(order => order.RatingResult == RatingResult.RatingFailed)
            .OrderBy(order => order.ReceivedAt)
            .ThenBy(order => order.Id)
            .AsNoTracking();
}
