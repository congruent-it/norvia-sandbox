using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.RatingExceptions;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

internal static class Samples
{
    public static readonly ShipperView BrouwerDranken = new("SHP-0001", "Brouwer Dranken");

    // 06:00–10:00 on 26 September 2026 in Rotterdam, which is summer time (UTC+2), from Rotterdam to Utrecht.
    public static TransportOrderToPlanSummary ToPlan(
        string number = "TO-0000042",
        string reference = "BD-2026-0042",
        RatingResult ratingResult = RatingResult.NotYetRated) => new(
        number,
        ratingResult,
        BrouwerDranken,
        reference,
        new PlaceView("3087 BM", "Rotterdam", "NL"),
        new PlaceView("3511 AW", "Utrecht", "NL"),
        new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.Zero));

    public static readonly CarrierView VanDijk = new("CAR-0001", "Van Dijk Transport");

    public static readonly CarrierView Kessler = new("CAR-0002", "Kessler Spedition");

    // Planned for Kessler Spedition at 11:45 on 24 September 2026 in Rotterdam (UTC+2).
    public static ShipmentToReleaseSummary ToRelease(string shipmentNumber = "SH-0000012", string transportOrderNumber = "TO-0000042") => new(
        shipmentNumber,
        transportOrderNumber,
        BrouwerDranken,
        new CarriedByView(AssignedTo.Carrier, Kessler),
        new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero));

    // Planned for Van Dijk Transport at 11:45 on 24 September 2026 in Rotterdam (UTC+2), then reassigned to Kessler
    // Spedition at 12:00.
    public static ShipmentView Shipment(
        string number = "SH-0000012",
        ShipmentStatus status = ShipmentStatus.Planned,
        IReadOnlyList<CarrierBookingView>? carrierBookings = null) => new(
        number,
        status,
        "TO-0000042",
        BrouwerDranken,
        new CarriedByView(AssignedTo.Carrier, Kessler),
        "corr-planning",
        new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero),
        [
            new ShipmentAssignmentView(new CarriedByView(AssignedTo.Carrier, VanDijk), "corr-planning", new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero)),
            new ShipmentAssignmentView(new CarriedByView(AssignedTo.Carrier, Kessler), "corr-reassignment", new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero)),
        ],
        carrierBookings ?? [],
        false);

    // Collection 06:00–10:00 and delivery 12:00–16:00 on 26 September 2026 in Rotterdam (UTC+2).
    public static TransportOrderView View(string number = "TO-0000042") => new(
        Guid.NewGuid(),
        number,
        TransportOrderStatus.Accepted,
        null,
        BrouwerDranken,
        IntakeChannel.ShipperApi,
        "corr-0042",
        new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.Zero),
        [],
        RatingResult.NotYetRated,
        null,
        null,
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

    // Light enough that the Lane's minimum charge applies, so the Freight Charge has both kinds of charge line.
    public static TransportOrderView RatedView(string number = "TO-0000042") => View(number) with
    {
        RatingResult = RatingResult.Rated,
        FreightCharge = new FreightChargeView(
            "EUR",
            65.00m,
            [new ChargeLineView("Freight", 38.40m), new ChargeLineView("MinimumChargeAdjustment", 26.60m)],
            new RateCardVersionView("RC-0001", 3)),
    };

    public static TransportOrderView RatingFailedView(string number = "TO-0000042") => View(number) with
    {
        RatingResult = RatingResult.RatingFailed,
        RatingFailure = new RatingFailureView("NoMatchingLane", "No Lane of Rate Card RC-0001 goes from Rotterdam to DE 40210."),
    };

    // Rating failed 11:31 on 24 September 2026 in Rotterdam (UTC+2), a minute after it was received.
    public static RatingExceptionSummary RatingException(
        string number = "TO-0000043",
        string reference = "BD-2026-0043",
        string code = "NoMatchingLane",
        string message = "Rate Card RC-0001 version 1 has no Lane from Rotterdam to Düsseldorf.") => new(
        Guid.NewGuid(),
        number,
        BrouwerDranken,
        reference,
        new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.Zero),
        new RatingFailureView(code, message),
        new DateTimeOffset(2026, 9, 24, 9, 31, 0, TimeSpan.Zero));

    // Received 11:30 on 24 September 2026 in Rotterdam (UTC+2).
    public static TransportOrderSummary RejectedSummary(string? reference = "BD-2026-0099") => new(
        Guid.NewGuid(),
        null,
        TransportOrderStatus.Rejected,
        null,
        BrouwerDranken,
        reference,
        null,
        null,
        new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.Zero));

    // A booking with no goods lines and a delivery window before its collection, as the Shipper submitted it.
    public static TransportOrderView RejectedView()
    {
        var submitted = View("TO-0000042").TransportOrder with { TransportOrderNumber = null, ShipperReference = "BD-2026-0099", GoodsLines = [] };
        return View("TO-0000042") with
        {
            TransportOrderNumber = null,
            Status = TransportOrderStatus.Rejected,
            CorrelationId = "corr-0099",
            RatingResult = null,
            RejectionReasons =
            [
                new RejectionReasonView("GoodsLinesMissing", "The booking has no goods lines."),
                new RejectionReasonView("DeliveryWindowBeforeCollection", "The delivery window ends before the collection window starts."),
            ],
            TransportOrder = submitted,
        };
    }
}
