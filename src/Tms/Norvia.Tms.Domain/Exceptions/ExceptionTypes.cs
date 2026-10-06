using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.Exceptions;

public static class ExceptionTypes
{
    /// <summary>The type of Exception a Tracking event of the type opens; null for one that opens none.</summary>
    public static ExceptionType? OpenedBy(TrackingEventType type) => type switch
    {
        TrackingEventType.CollectionFailed => ExceptionType.CollectionFailed,
        TrackingEventType.DeliveryFailed => ExceptionType.DeliveryFailed,
        TrackingEventType.Damaged => ExceptionType.Damaged,
        _ => null,
    };

    /// <summary>The type of Exception a Proof of delivery in the condition opens; null for a clean one.</summary>
    public static ExceptionType? OpenedBy(DeliveryCondition condition) => condition switch
    {
        DeliveryCondition.Reservations => ExceptionType.Reservations,
        _ => null,
    };
}
