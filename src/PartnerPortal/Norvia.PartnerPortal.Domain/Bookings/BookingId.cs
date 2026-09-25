namespace Norvia.PartnerPortal.Domain.Bookings;

public readonly record struct BookingId(Guid Value)
{
    public static BookingId New() => new(Guid.CreateVersion7());
}
