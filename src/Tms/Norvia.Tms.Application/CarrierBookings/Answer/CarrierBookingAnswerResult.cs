using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>What recording the Carrier's answer changed: the Carrier booking's new status and the Shipment's.</summary>
/// <param name="CorrelationId">The answer's own trail; for a repeat, the trail of the answer it repeats.</param>
/// <param name="Repeat">True when the answer was sent again with the same outcome and content, and answered as before without recording anything again.</param>
public sealed record CarrierBookingAnswerResult(
    string CarrierBookingNumber,
    CarrierBookingStatus Status,
    string ShipmentNumber,
    ShipmentStatus ShipmentStatus,
    string CorrelationId,
    bool Repeat = false)
{
    /// <summary>
    /// Builds the result from the booking and Shipment a Confirm or Reject handler just answered or found already
    /// answered: the booking's <c>AnswerCorrelationId</c> is set either way, freshly for a new answer, or, for a repeat,
    /// from the answer it repeats.
    /// </summary>
    public static CarrierBookingAnswerResult From(CarrierBooking booking, Shipment shipment, bool repeat) => new(
        booking.Number.Value, booking.Status, shipment.Number.Value, shipment.Status, booking.AnswerCorrelationId!.Value, repeat);
}
