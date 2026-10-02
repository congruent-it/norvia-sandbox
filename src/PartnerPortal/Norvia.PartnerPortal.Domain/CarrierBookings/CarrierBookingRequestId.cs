namespace Norvia.PartnerPortal.Domain.CarrierBookings;

public readonly record struct CarrierBookingRequestId(Guid Value)
{
    public static CarrierBookingRequestId New() => new(Guid.CreateVersion7());
}
