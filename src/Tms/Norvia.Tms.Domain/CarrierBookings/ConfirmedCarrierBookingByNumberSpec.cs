using Ardalis.Specification;
using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// The Confirmed Carrier booking of one Carrier with the TMS's Carrier booking number: how a Tracking event finds its
/// Shipment when the Carrier gave no booking reference that matched (P5).
/// </summary>
public sealed class ConfirmedCarrierBookingByNumberSpec : SingleResultSpecification<CarrierBooking>
{
    public ConfirmedCarrierBookingByNumberSpec(CarrierId carrierId, CarrierBookingNumber number)
    {
        CarrierId = carrierId;
        Number = number;
        Query.Where(booking =>
            booking.CarrierId == carrierId
            && booking.Number == number
            && booking.Status == CarrierBookingStatus.Confirmed)
            .AsNoTracking();
    }

    public CarrierId CarrierId { get; }

    public CarrierBookingNumber Number { get; }
}
