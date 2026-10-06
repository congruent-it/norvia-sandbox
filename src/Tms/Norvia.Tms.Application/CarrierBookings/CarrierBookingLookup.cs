using Ardalis.SharedKernel;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings;

/// <summary>
/// The Carrier booking a call names, or the refusal for an unknown booking number: shared by every Carrier booking
/// operation (the Carrier's answer, the P4 booking Logic App's reports, the Planner's withdrawal).
/// </summary>
internal static class CarrierBookingLookup
{
    public static async Task<(CarrierBooking? Booking, CarrierBookingRefusal? Refusal)> ToBookingAsync(
        IRepository<CarrierBooking> carrierBookings, string carrierBookingNumber, CancellationToken cancellationToken)
    {
        if (!CarrierBookingNumber.TryParse(carrierBookingNumber, out var number)
            || await carrierBookings.SingleOrDefaultAsync(new CarrierBookingByNumberSpec(number), cancellationToken) is not { } booking)
        {
            return (null, CarrierBookingRules.BookingNotFound(carrierBookingNumber));
        }

        return (booking, null);
    }

    /// <summary>As <see cref="ToBookingAsync"/>, and refuses a booking that isn't Withdrawn or Cancelled, the only ones the Carrier is told about (P7).</summary>
    public static async Task<(CarrierBooking? Booking, CarrierBookingRefusal? Refusal)> ToCancelledBookingAsync(
        IRepository<CarrierBooking> carrierBookings, string carrierBookingNumber, CancellationToken cancellationToken)
    {
        var (booking, refusal) = await ToBookingAsync(carrierBookings, carrierBookingNumber, cancellationToken);
        return booking is { Status: not (CarrierBookingStatus.Withdrawn or CarrierBookingStatus.Cancelled) }
            ? (null, CarrierBookingRules.NotCancelled(booking))
            : (booking, refusal);
    }
}
