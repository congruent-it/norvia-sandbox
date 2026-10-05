namespace Norvia.Tms.Domain.TrackingEvents;

public static class TrackingEventTypeExtensions
{
    /// <summary>True for an exception, which is recorded and published but never changes a Shipment's status.</summary>
    public static bool IsException(this TrackingEventType type) =>
        type is TrackingEventType.CollectionFailed or TrackingEventType.DeliveryFailed or TrackingEventType.Damaged;
}
