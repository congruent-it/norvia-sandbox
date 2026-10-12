using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>Reads the outcome of each Stop of Trips from the Tracking events they recorded (P12).</summary>
public sealed class TripOutcomes(IReadRepository<TrackingEvent> trackingEvents)
{
    /// <summary>The Stop outcomes of each of these Trips; a Trip without a Driver's report has every Stop Open.</summary>
    public async Task<IReadOnlyDictionary<TripNumber, StopOutcomes>> OfAsync(IReadOnlyCollection<TripNumber> trips, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trips);
        if (trips.Count == 0)
        {
            return new Dictionary<TripNumber, StopOutcomes>();
        }

        var recorded = await trackingEvents.ListAsync(new TrackingEventsOfTripSpec(trips), cancellationToken);
        return recorded
            .GroupBy(trackingEvent => trackingEvent.TripNumber ?? throw new InvalidOperationException("The specification selects the events of Trips."))
            .ToDictionary(group => group.Key, group => StopOutcomes.From(group));
    }
}
