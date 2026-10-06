using Norvia.Tms.Domain.Carriers;
using static System.FormattableString;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// P4's Carrier booking rules: BookingNotFound and NotOpen guard every operation on a booking (the Carrier's answer, the
/// P4 booking Logic App's reports, the Planner's withdrawal); CheckCarrier is the answer's own, as only the Carrier
/// booking's own Carrier may answer it. Whether an answer that doesn't pass NotOpen is a repeat of what's already
/// recorded, which succeeds instead of failing with <c>BookingNotOpen</c>, is for the caller to check against the
/// specific answer, with <see cref="CarrierBooking.IsRepeatOfConfirm"/> or <see cref="CarrierBooking.IsRepeatOfReject"/>.
/// </summary>
public static class CarrierBookingRules
{
    public static CarrierBookingRefusal BookingNotFound(string carrierBookingNumber) =>
        new(CarrierBookingRefusalCode.BookingNotFound, Invariant($"Carrier booking {carrierBookingNumber} is not known to the TMS."));

    /// <summary>Returns the wrong-Carrier refusal (no stable code, per the design's answer rules), or null when the Carrier answering is the booking's own.</summary>
    public static CarrierBookingRefusal? CheckCarrier(CarrierBooking booking, CarrierId carrierId)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(carrierId);
        return booking.CarrierId == carrierId
            ? null
            : new(Code: null, Invariant($"Carrier booking {booking.Number} belongs to Carrier {booking.CarrierId}, not {carrierId}."));
    }

    /// <summary>The refusal for an operation on a booking that isn't Requested (and, for an answer, isn't a repeat of what's already recorded).</summary>
    public static CarrierBookingRefusal NotOpen(CarrierBooking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(CarrierBookingRefusalCode.BookingNotOpen, Invariant($"Carrier booking {booking.Number} is {booking.Status}, not Requested."));
    }

    /// <summary>The refusal for a cancellation report on a booking that is neither Withdrawn nor Cancelled (P7).</summary>
    public static CarrierBookingRefusal NotCancelled(CarrierBooking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(CarrierBookingRefusalCode.BookingNotCancelled, Invariant($"Carrier booking {booking.Number} is {booking.Status}, not Withdrawn or Cancelled."));
    }
}
