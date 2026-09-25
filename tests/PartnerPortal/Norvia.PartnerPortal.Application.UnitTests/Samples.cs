using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests;

internal static class Samples
{
    /// <summary>A Thursday morning in Rotterdam (UTC+2).</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 24, 8, 15, 0, TimeSpan.Zero);

    public static readonly SavedAddress Warehouse = new("Brouwer Dranken warehouse", "Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL", Consignee: null);

    public static readonly SavedAddress Cafe = new(
        "Café De Zwaan", "Oudegracht 112", "3511 AW", "Utrecht", "NL", new SavedConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142"));

    public static readonly Persona BrouwerDranken = new(new PersonaId("brouwer-dranken"), "Brouwer Dranken", PartyType.Shipper, "SHP-0001", [Warehouse, Cafe]);

    public static readonly Persona VanDijkTransport = new(new PersonaId("van-dijk-transport"), "Van Dijk Transport", PartyType.Carrier, "CAR-0001", []);

    public static BookingRequest Booking(string shipperReference = "BD-2030-0001") => new()
    {
        ShipperReference = shipperReference,
        CollectionAddress = new BookingAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
        CollectionWindow = new LocalWindow(new DateTime(2030, 10, 1, 8, 0, 0), new DateTime(2030, 10, 1, 12, 0, 0)),
        Consignee = new BookingConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142"),
        DeliveryAddress = new BookingAddress("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
        GoodsLines = [new BookingGoodsLine(PackageType.Pallet, 2, 850.5m, new PackageDimensions(120, 80, 150))],
    };
}
