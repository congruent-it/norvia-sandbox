using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// The Canonical Model event <c>CarrierBookingNotDelivered</c>, version 1
/// (contracts/canonical-model/carrier-booking-not-delivered.v1.schema.json). The P4 booking Logic App's request never
/// reached the Carrier; the Carrier booking became NotDelivered and the Shipment went back to Planned, which carries no
/// event of its own: this event already says it.
/// </summary>
public sealed record CarrierBookingNotDeliveredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierBookingNumber,
    string ShipmentNumber,
    string CarrierId,
    int StatusCode,
    string Message)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierBookingNotDelivered";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static CarrierBookingNotDeliveredV1 From(CarrierBookingNotDeliveredEvent notDelivered)
    {
        ArgumentNullException.ThrowIfNull(notDelivered);
        var booking = notDelivered.Booking;
        return new(
            Guid.CreateVersion7(),
            booking.NotDeliveredAt!.Value,
            notDelivered.CorrelationId.Value,
            booking.Number.Value,
            booking.ShipmentNumber.Value,
            booking.CarrierId.Value,
            booking.NotDeliveredStatusCode!.Value,
            booking.NotDeliveredMessage!);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.CarrierBookingReport),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber),
    ];
}
