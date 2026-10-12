using Ardalis.Specification;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>The Tracking events the Driver reported on a Trip (P12), from which its Stops' outcomes are read.</summary>
public sealed class TrackingEventsOfTripSpec : Specification<TrackingEvent>
{
    public TrackingEventsOfTripSpec(TripNumber trip) : this([trip])
    {
    }

    public TrackingEventsOfTripSpec(IReadOnlyCollection<TripNumber> trips)
    {
        ArgumentNullException.ThrowIfNull(trips);
        Query.Where(trackingEvent => trackingEvent.TripNumber != null && trips.Contains(trackingEvent.TripNumber)).AsNoTracking();
    }
}
