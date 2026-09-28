using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings;

public sealed record CarrierBookingView(string CarrierBookingNumber, CarrierBookingStatus Status)
{
    public static CarrierBookingView Of(CarrierBooking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(booking.Number.Value, booking.Status);
    }
}
