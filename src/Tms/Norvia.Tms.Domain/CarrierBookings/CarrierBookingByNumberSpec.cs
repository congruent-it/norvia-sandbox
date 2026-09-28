using Ardalis.Specification;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>A Carrier booking to change: tracked, so recording its answer saves the change with it.</summary>
public sealed class CarrierBookingByNumberSpec : SingleResultSpecification<CarrierBooking>
{
    public CarrierBookingByNumberSpec(CarrierBookingNumber number)
    {
        Number = number;
        Query.Where(booking => booking.Number == number);
    }

    public CarrierBookingNumber Number { get; }
}
