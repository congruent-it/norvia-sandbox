using Norvia.PartnerPortal.Application.Bookings;

namespace Norvia.PartnerPortal.Web.Bookings;

public sealed class ConsigneeForm
{
    public string? Name { get; set; }

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public void Fill(BookingConsignee consignee)
    {
        ArgumentNullException.ThrowIfNull(consignee);
        (Name, ContactName, Phone, Email) = (consignee.Name, consignee.ContactName, consignee.Phone, consignee.Email);
    }

    public BookingConsignee ToConsignee() => new(Name, ContactName, Phone, Email);
}
