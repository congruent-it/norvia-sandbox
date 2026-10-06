using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// One cause of an Exception (P7): the Tracking event or Proof of delivery that opened it, or a later one of the same type
/// that joined it. It knows the type of Exception it causes, so an Exception takes any cause the same way.
/// </summary>
/// <param name="Kind">Whether the cause is a Tracking event or a Proof of delivery.</param>
/// <param name="CauseId">The ID of the Tracking event or Proof of delivery.</param>
/// <param name="SourceEventId">The sender's own ID for the cause: the Carrier's event ID of a Tracking event, or the document ID of a Proof of delivery.</param>
public sealed record ExceptionCause(
    ExceptionType Type, ExceptionCauseKind Kind, Guid CauseId, string SourceEventId, CorrelationId CorrelationId, DateTimeOffset AddedAt)
{
    /// <summary>The cause a Tracking event is; null for one that causes no Exception.</summary>
    public static ExceptionCause? From(TrackingEvent trackingEvent, DateTimeOffset addedAt)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);
        return ExceptionTypes.OpenedBy(trackingEvent.Type) is { } type
            ? new(type, ExceptionCauseKind.TrackingEvent, trackingEvent.Id.Value, trackingEvent.SourceEventId, trackingEvent.CorrelationId, addedAt)
            : null;
    }

    /// <summary>The cause a Proof of delivery is; null for one that causes no Exception.</summary>
    public static ExceptionCause? From(ProofOfDelivery proofOfDelivery, DateTimeOffset addedAt)
    {
        ArgumentNullException.ThrowIfNull(proofOfDelivery);
        return ExceptionTypes.OpenedBy(proofOfDelivery.Condition) is { } type
            ? new(type, ExceptionCauseKind.ProofOfDelivery, proofOfDelivery.Id.Value, proofOfDelivery.DocumentId, proofOfDelivery.CorrelationId, addedAt)
            : null;
    }
}
