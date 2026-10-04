namespace Norvia.Tms.Domain.TrackingEvents;

public readonly record struct TrackingEventId(Guid Value)
{
    public static TrackingEventId New() => new(Guid.CreateVersion7());
}
