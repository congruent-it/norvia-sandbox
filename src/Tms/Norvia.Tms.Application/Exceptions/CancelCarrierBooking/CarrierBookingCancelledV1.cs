using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.Exceptions.CancelCarrierBooking;

/// <summary>
/// The Canonical Model event <c>CarrierBookingCancelled</c>, version 1 (contracts/canonical-model/carrier-booking-cancelled.v1.schema.json).
/// The Planner ended a Confirmed Carrier booking before collection, from an Exception (P7, ADR 0010): the Carrier booking became
/// Cancelled and the Shipment went back to Planned, which carries no event of its own because this event already says it. The
/// Carrier is told from this event.
/// </summary>
/// <param name="CarrierBookingReference">The Carrier's own number for the booking, from its confirmation.</param>
/// <param name="ExceptionNumber">The Exception the Planner cancelled it from.</param>
/// <param name="ReasonText">Why Norvia cancelled it.</param>
public sealed record CarrierBookingCancelledV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierBookingNumber,
    string CarrierBookingReference,
    string ShipmentNumber,
    string CarrierId,
    string ExceptionNumber,
    string ReasonText)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierBookingCancelled";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static CarrierBookingCancelledV1 From(CarrierBookingCancelledEvent cancelled)
    {
        ArgumentNullException.ThrowIfNull(cancelled);
        var booking = cancelled.Booking;
        return new(
            Guid.CreateVersion7(),
            booking.CancelledAt ?? throw NotCancelled(booking),
            booking.CancellationCorrelationId?.Value ?? throw NotCancelled(booking),
            booking.Number.Value,
            booking.CarrierBookingReference ?? throw NotCancelled(booking),
            booking.ShipmentNumber.Value,
            booking.CarrierId.Value,
            booking.CancellationExceptionNumber?.Value ?? throw NotCancelled(booking),
            booking.CancellationReasonText ?? throw NotCancelled(booking));
    }

    private static InvalidOperationException NotCancelled(CarrierBooking booking) =>
        new($"Carrier booking {booking.Number} raised CarrierBookingCancelled without its cancellation or confirmation recorded.");

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.ExceptionHandling),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber),
    ];
}
