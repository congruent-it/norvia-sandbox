using Ardalis.Specification;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

public sealed class TripByIdempotencyKeySpec : Specification<Trip>
{
    public TripByIdempotencyKeySpec(IdempotencyKey key)
    {
        Query.Where(trip => trip.IdempotencyKey == key).AsNoTracking();
    }
}
