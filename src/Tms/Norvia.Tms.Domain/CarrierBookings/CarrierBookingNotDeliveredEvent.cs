using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <param name="correlationId">The report's own trail, which continues the release's (P4).</param>
public sealed class CarrierBookingNotDeliveredEvent(CarrierBooking booking, CorrelationId correlationId) : DomainEventBase
{
    public CarrierBooking Booking { get; } = booking;

    public CorrelationId CorrelationId { get; } = correlationId;
}
