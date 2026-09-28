using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <param name="transportOrderNumber">The Transport Order the confirmed Shipment carries out.</param>
public sealed class CarrierBookingConfirmedEvent(CarrierBooking booking, TransportOrderNumber transportOrderNumber) : DomainEventBase
{
    public CarrierBooking Booking { get; } = booking;

    public TransportOrderNumber TransportOrderNumber { get; } = transportOrderNumber;
}
