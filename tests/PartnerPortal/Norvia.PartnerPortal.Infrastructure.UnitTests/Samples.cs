using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests;

internal static class Samples
{
    public static readonly Persona BrouwerDranken = new(new PersonaId("brouwer-dranken"), "Brouwer Dranken", PartyType.Shipper, "SHP-0001", []);

    public static readonly Persona VanDijkTransport = new(new PersonaId("van-dijk-transport"), "Van Dijk Transport", PartyType.Carrier, "CAR-0001", []);

    /// <summary>Every field filled in, as in contracts/partner-api/samples/bookings/full.booking.json.</summary>
    public static BookingRequest FullBooking() => new()
    {
        ShipperReference = "BD-2030-0002",
        CollectionAddress = new BookingAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
        CollectionWindow = new LocalWindow(new DateTime(2030, 10, 2, 7, 30, 0), new DateTime(2030, 10, 2, 11, 0, 0)),
        Consignee = new BookingConsignee("Getränke Schmitz GmbH", "Jonas Schmitz", "+49 211 555 0190", "wareneingang@getraenke-schmitz.example"),
        DeliveryAddress = new BookingAddress("Kölner Landstraße 301", "40591", "Düsseldorf", "DE"),
        DeliveryWindow = new LocalWindow(new DateTime(2030, 10, 3, 8, 0, 0), new DateTime(2030, 10, 3, 16, 30, 0)),
        GoodsLines =
        [
            new BookingGoodsLine(PackageType.Pallet, 4, 2400m, new PackageDimensions(120, 80, 160)),
            new BookingGoodsLine(PackageType.Crate, 10, 180.25m, new PackageDimensions(60, 40, 35.5m)),
            new BookingGoodsLine(PackageType.Parcel, 3, 12m),
            new BookingGoodsLine(PackageType.Loose, 1, 35m),
        ],
        Instructions = "Deliver at dock 3. Call Jonas 30 minutes before arrival.",
    };
}
