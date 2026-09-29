using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Withdraw;

/// <summary>
/// The Canonical Model event <c>CarrierBookingWithdrawn</c>, version 1
/// (contracts/canonical-model/carrier-booking-withdrawn.v1.schema.json). The Planner took a Requested Carrier booking
/// back before the Carrier answered; the Carrier booking became Withdrawn and the Shipment went back to Planned, which
/// carries no event of its own: this event already says it.
/// </summary>
public sealed record CarrierBookingWithdrawnV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, string CarrierBookingNumber, string ShipmentNumber, string CarrierId)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierBookingWithdrawn";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static CarrierBookingWithdrawnV1 From(CarrierBookingWithdrawnEvent withdrawn)
    {
        ArgumentNullException.ThrowIfNull(withdrawn);
        var booking = withdrawn.Booking;
        return new(
            Guid.CreateVersion7(), booking.WithdrawnAt!.Value, booking.WithdrawalCorrelationId!.Value, booking.Number.Value, booking.ShipmentNumber.Value, booking.CarrierId.Value);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TransportPlanning),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber),
    ];
}
