using Ardalis.SharedKernel;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>
/// The Carrier booking a caller's answer names, and the Carrier answering: checked once, so both
/// <see cref="ConfirmCarrierBookingHandler"/> and <see cref="RejectCarrierBookingHandler"/> refuse an unknown booking
/// number or the wrong Carrier the same way.
/// </summary>
internal static class CarrierBookingAnswerLookup
{
    /// <summary>
    /// The booking the answer names, or the refusal for an unknown booking number or the wrong Carrier answering. Only
    /// whether the booking is Requested, and whether the answer is a repeat, are for the caller to check next.
    /// </summary>
    public static async Task<(CarrierBooking? Booking, CarrierBookingRefusal? Refusal)> ToAnswerAsync(
        IRepository<CarrierBooking> carrierBookings, string carrierBookingNumber, string carrierId, CancellationToken cancellationToken)
    {
        if (!CarrierBookingNumber.TryParse(carrierBookingNumber, out var number)
            || await carrierBookings.SingleOrDefaultAsync(new CarrierBookingByNumberSpec(number), cancellationToken) is not { } booking)
        {
            return (null, CarrierBookingAnswerRules.BookingNotFound(carrierBookingNumber));
        }

        return CarrierBookingAnswerRules.CheckCarrier(booking, new CarrierId(carrierId)) is { } wrongCarrier
            ? (null, wrongCarrier)
            : (booking, null);
    }
}
