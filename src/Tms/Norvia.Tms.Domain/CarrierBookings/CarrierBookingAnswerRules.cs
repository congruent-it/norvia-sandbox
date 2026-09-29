using Norvia.Tms.Domain.Carriers;
using static System.FormattableString;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// P4's answer rules, checked before the Carrier's confirmation or rejection is recorded. Whether an answer that
/// doesn't pass is a repeat of what's already recorded, which succeeds instead of failing with <c>BookingNotOpen</c>,
/// is for the caller to check against the specific answer, with <see cref="CarrierBooking.IsRepeatOfConfirm"/> or
/// <see cref="CarrierBooking.IsRepeatOfReject"/>.
/// </summary>
public static class CarrierBookingAnswerRules
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

    /// <summary>The refusal for an answer to a booking that isn't Requested and isn't a repeat of what's already recorded.</summary>
    public static CarrierBookingRefusal NotOpen(CarrierBooking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(CarrierBookingRefusalCode.BookingNotOpen, Invariant($"Carrier booking {booking.Number} is {booking.Status}, not Requested."));
    }
}
