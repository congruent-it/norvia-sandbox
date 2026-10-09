using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>
/// The Canonical Model event <c>CarrierBookingConfirmed</c>, version 1
/// (contracts/canonical-model/carrier-booking-confirmed.v1.schema.json). The Carrier confirmed a Requested Carrier
/// booking with its own booking reference and, optionally, a planned collection time, and with the total of the Carrier cost it accepted (P9); the Carrier booking became
/// Confirmed and the Shipment Booked, which carries no event of its own.
/// </summary>
public sealed record CarrierBookingConfirmedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierBookingNumber,
    string ShipmentNumber,
    string TransportOrderNumber,
    string CarrierId,
    string CarrierBookingReference,
    DateTimeOffset? PlannedCollectionTime,
    decimal? CarrierCostTotal = null)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierBookingConfirmed";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static CarrierBookingConfirmedV1 From(CarrierBookingConfirmedEvent confirmed)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        var booking = confirmed.Booking;
        return new(
            Guid.CreateVersion7(),
            booking.AnsweredAt!.Value,
            booking.AnswerCorrelationId!.Value,
            booking.Number.Value,
            booking.ShipmentNumber.Value,
            confirmed.TransportOrderNumber.Value,
            booking.CarrierId.Value,
            booking.CarrierBookingReference!,
            booking.PlannedCollectionTime,
            booking.CarrierCost?.Total.Amount);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties()
    {
        List<KeyValuePair<string, object>> properties =
        [
            new(TrackingKeys.Flow, Flows.CarrierBookingAnswer),
            new(TrackingKeys.PartyId, CarrierId),
            new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
            new(TrackingKeys.ShipmentNumber, ShipmentNumber),
            new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber),
        ];
        return properties;
    }
}
