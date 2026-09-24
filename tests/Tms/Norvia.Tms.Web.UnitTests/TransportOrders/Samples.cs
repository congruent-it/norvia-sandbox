using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

internal static class Samples
{
    public static readonly ShipperView BrouwerDranken = new("SHP-0001", "Brouwer Dranken");

    // 06:00–10:00 on 26 September 2026 in Rotterdam, which is summer time (UTC+2).
    public static TransportOrderSummary Summary(string number = "TO-0000042", string reference = "BD-2026-0042") => new(
        Guid.NewGuid(),
        number,
        TransportOrderStatus.Accepted,
        BrouwerDranken,
        reference,
        new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.Zero));

    // Collection 06:00–10:00 and delivery 12:00–16:00 on 26 September 2026 in Rotterdam (UTC+2).
    public static TransportOrderView View(string number = "TO-0000042") => new(
        Guid.NewGuid(),
        number,
        TransportOrderStatus.Accepted,
        BrouwerDranken,
        IntakeChannel.ShipperApi,
        "corr-0042",
        new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.Zero),
        [],
        new CanonicalTransportOrder(
            number,
            BrouwerDranken.Id,
            "BD-2026-0042",
            new CanonicalCollectionDetails(
                new CanonicalAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
                new CanonicalTimeWindow(new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero))),
            new CanonicalDeliveryDetails(
                new CanonicalAddress("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
                new CanonicalConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", "sanne@dezwaan.example"),
                new CanonicalTimeWindow(new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 26, 14, 0, 0, TimeSpan.Zero))),
            [
                new CanonicalGoodsLine("pallet", 2, 850.5m, new CanonicalDimensions(120, 80, 150)),
                new CanonicalGoodsLine("crate", 4, 60m, null),
            ],
            "Ring at the side entrance"));
}
