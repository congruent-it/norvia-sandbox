using Ardalis.SharedKernel;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>
/// The Carrier booking a caller names, tracked, so recording its answer saves the change. An unknown booking number
/// throws for now; a later ticket turns it into a `BookingNotFound` refusal.
/// </summary>
internal static class CarrierBookingLookup
{
    public static async Task<CarrierBooking> ToChangeAsync(
        IRepository<CarrierBooking> carrierBookings, string carrierBookingNumber, CancellationToken cancellationToken)
    {
        if (!CarrierBookingNumber.TryParse(carrierBookingNumber, out var number)
            || await carrierBookings.SingleOrDefaultAsync(new CarrierBookingByNumberSpec(number), cancellationToken) is not { } booking)
        {
            throw new InvalidOperationException($"Carrier booking {carrierBookingNumber} is not known to the TMS.");
        }

        return booking;
    }
}
