using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.UnitTests;

internal static class Samples
{
    public static readonly DateTimeOffset SentAt = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    public static readonly PersonaView BrouwerDranken = new("brouwer-dranken", "Brouwer Dranken", PartyType.Shipper, TimeSpan.FromSeconds(3));

    public static readonly PersonaView VanDijkTransport = new("van-dijk-transport", "Van Dijk Transport", PartyType.Carrier);

    public static IReadOnlyList<PersonaView> Personas => [BrouwerDranken, VanDijkTransport];

    public static BookingRequest Draft() => new()
    {
        ShipperReference = "BK-20260925-100000",
        CollectionAddress = new BookingAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
        CollectionWindow = new LocalWindow(new DateTime(2026, 9, 26, 8, 0, 0), new DateTime(2026, 9, 26, 12, 0, 0)),
        Consignee = new BookingConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142"),
        DeliveryAddress = new BookingAddress("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
        GoodsLines = [new BookingGoodsLine(PackageType.Pallet, 2, 850m, new PackageDimensions(120, 80, 150))],
    };

    public static BookingView Answered(NorviaAnswer answer, string? shipperReference = "BK-20260925-100000") =>
        new(Guid.CreateVersion7(), shipperReference, SentAt, answer.Result, answer.TransportOrderNumber, answer.Reasons, answer.Text, answer.MessageId);
}
