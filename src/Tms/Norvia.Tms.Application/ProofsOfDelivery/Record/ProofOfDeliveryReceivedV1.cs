using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.ProofsOfDelivery;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

/// <summary>
/// The Canonical Model event <c>ProofOfDeliveryReceived</c>, version 1 (contracts/canonical-model/proof-of-delivery-received.v1.schema.json).
/// A Proof of delivery was recorded as a Shipment's current one (P6). It carries no file content and no link: consumers
/// read the files through the TMS.
/// </summary>
/// <param name="OccurredAt">When the TMS recorded the Proof of delivery.</param>
/// <param name="CarrierId">The Carrier that sent it; null for Own fleet.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking; null for Own fleet, which names its Trip instead (P12).</param>
/// <param name="TripNumber">The Trip the Driver reported it on (P12); null for a Carrier's.</param>
/// <param name="DeliveryTime">When the goods were delivered, with the offset of where.</param>
/// <param name="Condition"><c>Clean</c> or <c>Reservations</c>.</param>
/// <param name="ReplacesEarlier">Whether it replaced an earlier current Proof of delivery.</param>
public sealed record ProofOfDeliveryReceivedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string? CarrierId,
    string? CarrierBookingReference,
    string? TripNumber,
    string ReceivedBy,
    DateTimeOffset DeliveryTime,
    string Condition,
    string? Remark,
    int NumberOfFiles,
    bool ReplacesEarlier)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ProofOfDeliveryReceived";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ProofOfDeliveryReceivedV1 From(ProofOfDeliveryReceivedEvent received)
    {
        ArgumentNullException.ThrowIfNull(received);
        var proofOfDelivery = received.ProofOfDelivery;
        return new(
            Guid.CreateVersion7(),
            proofOfDelivery.ReceivedAt,
            proofOfDelivery.CorrelationId.Value,
            received.Shipment.Number.Value,
            received.Shipment.TransportOrderNumber.Value,
            proofOfDelivery.Source.CarrierId?.Value,
            proofOfDelivery.CarrierBookingReference,
            proofOfDelivery.TripNumber?.Value,
            proofOfDelivery.ReceivedBy,
            proofOfDelivery.DeliveryTime,
            proofOfDelivery.Condition.ToString(),
            proofOfDelivery.Remark,
            proofOfDelivery.Files.Count,
            received.ReplacesEarlier);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties()
    {
        var properties = new List<KeyValuePair<string, object>>
        {
            new(TrackingKeys.Flow, TripNumber is null ? Flows.CarrierProofOfDelivery : Flows.TripExecution),
            new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
            new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        };
        if (CarrierId is not null)
        {
            properties.Add(new(TrackingKeys.PartyId, CarrierId));
        }

        if (TripNumber is not null)
        {
            properties.Add(new(TrackingKeys.TripNumber, TripNumber));
        }

        return properties;
    }

    protected override IEnumerable<KeyValuePair<string, object>> FilterProperties() =>
    [
        new(ShipmentEventProperties.AssignedTo, (CarrierId is null ? AssignedTo.OwnFleet : AssignedTo.Carrier).ToString()),
    ];
}
