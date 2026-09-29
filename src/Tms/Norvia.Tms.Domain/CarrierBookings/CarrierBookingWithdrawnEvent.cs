using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.CarrierBookings;

public sealed class CarrierBookingWithdrawnEvent(CarrierBooking booking) : DomainEventBase
{
    public CarrierBooking Booking { get; } = booking;
}
