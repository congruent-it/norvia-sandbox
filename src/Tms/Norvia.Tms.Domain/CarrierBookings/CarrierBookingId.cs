namespace Norvia.Tms.Domain.CarrierBookings;

public readonly record struct CarrierBookingId(Guid Value)
{
    public static CarrierBookingId New() => new(Guid.CreateVersion7());
}
