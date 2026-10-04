using Ardalis.Specification;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>The Tracking event a source already reported under its own event ID, so a repeat of it is recognised.</summary>
public sealed class TrackingEventBySourceEventIdSpec : SingleResultSpecification<TrackingEvent>
{
    public TrackingEventBySourceEventIdSpec(TrackingSource source, string sourceEventId)
    {
        Source = source;
        SourceEventId = sourceEventId;
        Query.Where(trackingEvent => trackingEvent.Source == source && trackingEvent.SourceEventId == sourceEventId).AsNoTracking();
    }

    public TrackingSource Source { get; }

    public string SourceEventId { get; }
}
