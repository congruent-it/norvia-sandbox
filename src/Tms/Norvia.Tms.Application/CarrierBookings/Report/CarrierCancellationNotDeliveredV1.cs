using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// The Canonical Model event <c>CarrierCancellationNotDelivered</c>, version 1
/// (contracts/canonical-model/carrier-cancellation-not-delivered.v1.schema.json). The Carrier cancellation Logic App's message for a
/// Cancelled or Withdrawn Carrier booking never reached the Carrier (P7): the booking is flagged Carrier not told, and the Planner
/// phones the Carrier. Nothing is retried after that.
/// </summary>
/// <param name="Kind">Whether Norvia ended the booking by <c>Withdrawn</c> or <c>Cancelled</c>.</param>
/// <param name="StatusCode">The Carrier endpoint's HTTP status code, or the technical failure's own code.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record CarrierCancellationNotDeliveredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierBookingNumber,
    string ShipmentNumber,
    string CarrierId,
    string Kind,
    int StatusCode,
    string Message)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierCancellationNotDelivered";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static CarrierCancellationNotDeliveredV1 From(CarrierCancellationNotDeliveredEvent notDelivered)
    {
        ArgumentNullException.ThrowIfNull(notDelivered);
        var booking = notDelivered.Booking;
        return new(
            Guid.CreateVersion7(),
            booking.CarrierNotToldAt ?? throw NotFlagged(booking),
            notDelivered.CorrelationId.Value,
            booking.Number.Value,
            booking.ShipmentNumber.Value,
            booking.CarrierId.Value,
            booking.Status.ToString(),
            booking.CarrierNotToldStatusCode ?? throw NotFlagged(booking),
            booking.CarrierNotToldMessage ?? throw NotFlagged(booking));
    }

    private static InvalidOperationException NotFlagged(CarrierBooking booking) =>
        new($"Carrier booking {booking.Number} raised CarrierCancellationNotDelivered without being flagged Carrier not told.");

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.ExceptionHandling),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber),
    ];
}
