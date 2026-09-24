using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

internal static class Samples
{
    public static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    public static CanonicalTransportOrder CanonicalTransportOrder() => new(
        TransportOrderNumber: null,
        ShipperId: "SHP-0001",
        ShipperReference: "BD-2026-0001",
        Collection: new CanonicalCollectionDetails(
            new CanonicalAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
            new CanonicalTimeWindow(Now.AddDays(1), Now.AddDays(1).AddHours(4))),
        Delivery: new CanonicalDeliveryDetails(
            new CanonicalAddress("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
            new CanonicalConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", null),
            new CanonicalTimeWindow(Now.AddDays(1).AddHours(6), Now.AddDays(1).AddHours(10))),
        GoodsLines:
        [
            new CanonicalGoodsLine("pallet", 2, 850m, new CanonicalDimensions(120m, 80m, 150m)),
            new CanonicalGoodsLine("crate", 4, 60.5m, null),
        ],
        Instructions: "Ring at the side entrance");

    public static readonly RejectionReason[] Reasons =
    [
        new(RejectionCode.InactiveShipper, "The Shipper is not active, so Norvia can't take bookings for it."),
        new(RejectionCode.GoodsLinesMissing, "The booking has no goods lines."),
    ];

    public static TransportOrder RejectedOrder(string correlationId = "corr-2") =>
        TransportOrder.Reject(
            new ShipperId("SHP-0001"),
            CanonicalTransportOrder().ToDetails(),
            Reasons,
            IntakeChannel.ShipperApi,
            new CorrelationId(correlationId),
            Now);

    public static TransportOrder AcceptedOrder(long sequence = 7, string correlationId = "corr-1") =>
        TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            CanonicalTransportOrder().ToDetails(),
            TransportOrderNumber.FromSequence(sequence),
            IntakeChannel.ShipperApi,
            new CorrelationId(correlationId),
            Now);
}
