using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Exceptions.Open;

/// <summary>
/// The Canonical Model event <c>ExceptionOpened</c>, version 1 (contracts/canonical-model/exception-opened.v1.schema.json).
/// The TMS opened an Exception for a Shipment (P7). It carries the correlation ID of the cause that opened it: a Tracking event or a Proof of delivery.
/// </summary>
/// <param name="OccurredAt">When the TMS opened the Exception.</param>
/// <param name="ShipperId">The Shipper of the Transport Order the Shipment carries out.</param>
/// <param name="CarrierId">The Carrier that carries the Shipment; null for Own fleet.</param>
/// <param name="ExceptionType">What went wrong: <c>CollectionFailed</c>, <c>DeliveryFailed</c>, <c>Damaged</c> or <c>Reservations</c>.</param>
/// <param name="Cause">What opened it.</param>
/// <param name="AfterDelivered">True when the Shipment was Delivered already when the cause arrived.</param>
public sealed record ExceptionOpenedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ExceptionNumber,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    string? CarrierId,
    string ExceptionType,
    ExceptionCauseV1 Cause,
    bool AfterDelivered)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ExceptionOpened";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ExceptionOpenedV1 From(ExceptionOpenedEvent opened)
    {
        ArgumentNullException.ThrowIfNull(opened);
        var exception = opened.Exception;
        return new(
            Guid.CreateVersion7(),
            exception.OpenedAt,
            exception.CorrelationId.Value,
            exception.Number.Value,
            exception.ShipmentNumber.Value,
            exception.TransportOrderNumber.Value,
            exception.ShipperId.Value,
            exception.CarrierId?.Value,
            exception.Type.ToString(),
            new ExceptionCauseV1(opened.Cause.IsProofOfDelivery ? ExceptionCauseV1.ProofOfDelivery : ExceptionCauseV1.TrackingEvent, opened.Cause.SourceEventId),
            exception.AfterDelivered);
    }

    // It is traced on the trail of the Tracking event or Proof of delivery that caused it, which is that flow's.
    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties()
    {
        var properties = new List<KeyValuePair<string, object>>
        {
            new(TrackingKeys.Flow, Cause.Kind == ExceptionCauseV1.ProofOfDelivery ? Flows.CarrierProofOfDelivery : Flows.CarrierTracking),
            new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
            new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        };
        if (CarrierId is not null)
        {
            properties.Add(new(TrackingKeys.PartyId, CarrierId));
        }

        if (Cause.SourceEventId is not null)
        {
            properties.Add(new(TrackingKeys.SourceMessageId, Cause.SourceEventId));
        }

        return properties;
    }

    protected override IEnumerable<KeyValuePair<string, object>> FilterProperties() =>
    [
        new(ShipmentEventProperties.AssignedTo, (CarrierId is null ? AssignedTo.OwnFleet : AssignedTo.Carrier).ToString()),
    ];
}
