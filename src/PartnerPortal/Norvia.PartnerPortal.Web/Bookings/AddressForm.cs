using Norvia.PartnerPortal.Application.Bookings;

namespace Norvia.PartnerPortal.Web.Bookings;

public sealed class AddressForm
{
    public string? Street { get; set; }

    public string? Postcode { get; set; }

    public string? City { get; set; }

    /// <summary>A country's name or its ISO code: the Norvia Partner API reads either.</summary>
    public string? Country { get; set; }

    public void Fill(BookingAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        (Street, Postcode, City, Country) = (address.Street, address.Postcode, address.City, address.Country);
    }

    public BookingAddress ToAddress() => new(Street, Postcode, City, Country);
}
