using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.Exceptions;

public static class ExceptionTypes
{
    /// <summary>The type of Exception a Tracking event of the type opens; null for one that opens none.</summary>
    public static ExceptionType? OpenedBy(TrackingEventType type) => type switch
    {
        TrackingEventType.DeliveryFailed => ExceptionType.DeliveryFailed,
        _ => null,
    };
}
