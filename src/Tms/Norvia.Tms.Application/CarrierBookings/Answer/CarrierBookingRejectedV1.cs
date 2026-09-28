using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>
/// The Canonical Model event <c>CarrierBookingRejected</c>, version 1
/// (contracts/canonical-model/carrier-booking-rejected.v1.schema.json). The Carrier rejected a Requested Carrier
/// booking with a reason; the Carrier booking became Rejected and the Shipment went back to Planned, which carries no
/// event of its own: this event already says it.
/// </summary>
public sealed record CarrierBookingRejectedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierBookingNumber,
    string ShipmentNumber,
    string CarrierId,
    string ReasonCode,
    string? ReasonText)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierBookingRejected";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static CarrierBookingRejectedV1 From(CarrierBookingRejectedEvent rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        var booking = rejected.Booking;
        return new(
            Guid.CreateVersion7(),
            booking.AnsweredAt!.Value,
            booking.AnswerCorrelationId!.Value,
            booking.Number.Value,
            booking.ShipmentNumber.Value,
            booking.CarrierId.Value,
            booking.RejectionReason!.Value.ToString(),
            booking.RejectionReasonText);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.CarrierBookingAnswer),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber),
        new(TrackingKeys.FailureCode, ReasonCode),
    ];
}
