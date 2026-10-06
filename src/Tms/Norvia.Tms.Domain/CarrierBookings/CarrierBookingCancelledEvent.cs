using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.CarrierBookings;

public sealed class CarrierBookingCancelledEvent(CarrierBooking booking) : DomainEventBase
{
    public CarrierBooking Booking { get; } = booking;
}
