using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// One cause of an Exception (P7): the Tracking event or Proof of delivery that opened it, or a later one of the same type
/// that joined it. Exactly one of <paramref name="TrackingEventId"/> and <paramref name="ProofOfDeliveryId"/> is set.
/// </summary>
/// <param name="SourceEventId">The sender's own ID for the cause: the Carrier's event ID of a Tracking event, or the document ID of a Proof of delivery.</param>
public sealed record ExceptionCause(
    TrackingEventId? TrackingEventId, ProofOfDeliveryId? ProofOfDeliveryId, string SourceEventId, CorrelationId CorrelationId, DateTimeOffset AddedAt)
{
    /// <summary>Whether the cause is a Proof of delivery, as opposed to a Tracking event.</summary>
    public bool IsProofOfDelivery => ProofOfDeliveryId is not null;

    public static ExceptionCause Of(TrackingEvent trackingEvent, DateTimeOffset addedAt)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);
        return new(trackingEvent.Id, null, trackingEvent.SourceEventId, trackingEvent.CorrelationId, addedAt);
    }

    public static ExceptionCause Of(ProofOfDelivery proofOfDelivery, DateTimeOffset addedAt)
    {
        ArgumentNullException.ThrowIfNull(proofOfDelivery);
        return new(null, proofOfDelivery.Id, proofOfDelivery.DocumentId, proofOfDelivery.CorrelationId, addedAt);
    }
}
