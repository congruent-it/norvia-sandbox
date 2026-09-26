using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// The Canonical Model event <c>TransportOrderRated</c>, version 1 (contracts/canonical-model/transport-order-rated.v1.schema.json).
/// </summary>
public sealed record TransportOrderRatedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TransportOrderNumber,
    string ShipperId,
    FreightChargeView FreightCharge)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TransportOrderRated";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TransportOrderEvents;

    /// <param name="correlationId">The Message trail the rating belongs to: the intake's, or a re-rate's own.</param>
    public static TransportOrderRatedV1 From(TransportOrder order, CorrelationId correlationId)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (order is not { Number: { } number, FreightCharge: { } freightCharge, RatedAt: { } ratedAt })
        {
            throw new ArgumentException("Only a Rated Transport Order has a Freight Charge.", nameof(order));
        }

        return new(
            Guid.CreateVersion7(),
            ratedAt,
            correlationId.Value,
            number.Value,
            order.ShipperId.Value,
            FreightChargeView.From(freightCharge)!);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TransportOrderRating),
        new(TrackingKeys.PartyId, ShipperId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
    ];
}
