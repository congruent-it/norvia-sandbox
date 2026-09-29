using Ardalis.Specification;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>The open Carrier bookings, waiting for the Carrier's answer (P4).</summary>
public sealed class RequestedCarrierBookingsSpec : Specification<CarrierBooking>
{
    public RequestedCarrierBookingsSpec() => Query.Where(booking => booking.Status == CarrierBookingStatus.Requested).AsNoTracking();
}
