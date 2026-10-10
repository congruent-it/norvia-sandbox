using Ardalis.Specification;

namespace Norvia.Tms.Domain.Trips;

public sealed class TripByNumberSpec : Specification<Trip>
{
    public TripByNumberSpec(TripNumber number)
    {
        Query.Where(trip => trip.Number == number);
    }
}
