using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// The Canonical Model event <c>TransportOrderRatingFailed</c>, version 1
/// (contracts/canonical-model/transport-order-rating-failed.v1.schema.json).
/// </summary>
public sealed record TransportOrderRatingFailedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TransportOrderNumber,
    string ShipperId,
    RatingFailureView RatingFailure)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TransportOrderRatingFailed";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TransportOrderEvents;

    /// <param name="correlationId">The Message trail the rating belongs to: the intake's, or a re-rate's own.</param>
    public static TransportOrderRatingFailedV1 From(TransportOrder order, CorrelationId correlationId)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (order is not { Number: { } number, RatingFailure: { } failure, RatingFailedAt: { } failedAt })
        {
            throw new ArgumentException("Only a Transport Order whose Rating failed has a rating failure.", nameof(order));
        }

        return new(
            Guid.CreateVersion7(),
            failedAt,
            correlationId.Value,
            number.Value,
            order.ShipperId.Value,
            RatingFailureView.From(failure)!);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TransportOrderRating),
        new(TrackingKeys.PartyId, ShipperId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.FailureCode, RatingFailure.Code),
    ];
}
