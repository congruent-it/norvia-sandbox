using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.CarrierBookings;

public sealed class CarrierBookingRejectedEvent(CarrierBooking booking) : DomainEventBase
{
    public CarrierBooking Booking { get; } = booking;
}
