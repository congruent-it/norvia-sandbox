using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.Release;

/// <summary>
/// The Canonical Model event <c>ShipmentReleased</c>, version 1 (contracts/canonical-model/shipment-released.v1.schema.json).
/// It carries the whole Shipment with its Transport Order's details and, for a Carrier Shipment, the Carrier booking
/// number the release created (P4) and its Carrier cost (P9), so P4's booking Logic App can act on the event alone, under the release's own
/// correlation ID.
/// </summary>
/// <param name="AssignedTo"><c>OwnFleet</c> or <c>Carrier</c>.</param>
/// <param name="CarrierId">The Carrier that carries the Shipment; null for Own fleet.</param>
/// <param name="CarrierBookingNumber">The Carrier booking the release created; null for Own fleet.</param>
/// <param name="TransportOrder">The Transport Order the Shipment carries out: its addresses, windows and goods lines.</param>
/// <param name="CarrierCost">The Carrier cost the release froze on the Carrier booking (P9); null for Own fleet.</param>
public sealed record ShipmentReleasedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    string AssignedTo,
    string? CarrierId,
    string? CarrierBookingNumber,
    CanonicalTransportOrder TransportOrder,
    CarrierCostView? CarrierCost = null)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipmentReleased";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipmentReleasedV1 From(ShipmentReleasedEvent released)
    {
        ArgumentNullException.ThrowIfNull(released);
        var shipment = released.Shipment;
        return new(
            Guid.CreateVersion7(),
            released.Release.ReleasedAt,
            released.Release.CorrelationId.Value,
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            shipment.ShipperId.Value,
            shipment.CarriedBy.AssignedTo().ToString(),
            shipment.CarriedBy.CarrierId?.Value,
            released.Release.CarrierBookingNumber?.Value,
            CanonicalTransportOrder.From(released.TransportOrder),
            CarrierCostView.From(released.CarrierCost));
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties()
    {
        var properties = new List<KeyValuePair<string, object>>
        {
            new(TrackingKeys.Flow, Flows.TransportPlanning),
            new(TrackingKeys.PartyId, ShipperId),
            new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
            new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        };
        if (CarrierBookingNumber is not null)
        {
            properties.Add(new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber));
        }

        return properties;
    }

    // P4's subscription filters on it, for Carrier Shipments only.
    protected override IEnumerable<KeyValuePair<string, object>> FilterProperties() =>
    [
        new(ShipmentEventProperties.AssignedTo, AssignedTo),
    ];
}
