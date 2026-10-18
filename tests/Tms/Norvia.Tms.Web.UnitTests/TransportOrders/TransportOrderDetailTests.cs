using AngleSharp.Dom;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetDetail;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class TransportOrderDetailTests : TmsWebContext
{
    [Fact]
    public void Render_AnAcceptedTransportOrder_ShowsEverythingTheShipperSubmitted()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.View("TO-0000042")));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => page.Find("h1").TextContent.Should().Be("Transport Order TO-0000042"));
        Definitions(page, "Transport Order").Should().Equal(new Dictionary<string, string>
        {
            ["Status"] = "Accepted",
            ["Shipper"] = "Brouwer Dranken",
            ["Shipper reference"] = "BD-2026-0042",
            ["Received"] = "Thu 24 Sep 2026, 11:30",
            ["Correlation ID"] = "corr-0042",
        });
        Definitions(page, "Collection").Should().Equal(new Dictionary<string, string>
        {
            ["Address"] = "Waalhaven Oostzijde 88, 3087 BM Rotterdam, NL",
            ["Window"] = "Sat 26 Sep 2026, 06:00–10:00",
        });
        Definitions(page, "Delivery").Should().Equal(new Dictionary<string, string>
        {
            ["Address"] = "Oudegracht 112, 3511 AW Utrecht, NL",
            ["Consignee"] = "Café De Zwaan",
            ["Contact"] = "Sanne de Vries",
            ["Phone"] = "+31 30 555 0142",
            ["Email"] = "sanne@dezwaan.example",
            ["Window"] = "Sat 26 Sep 2026, 12:00–16:00",
        });
        Rows(page, "Goods").Should().Equal("pallet | 2 | 850.5 | 120 × 80 × 150", "crate | 4 | 60 | Not given");
        Section(page, "Instructions").TextContent.Should().Contain("Ring at the side entrance");
    }

    [Fact]
    public void Render_APlannedTransportOrder_ShowsItsShipmentLinkingToIt()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns(Detail(Samples.View("TO-0000042") with { Status = TransportOrderStatus.Planned, ShipmentNumber = "SH-0000012" }));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Transport Order").Should().Contain(new Dictionary<string, string>
        {
            ["Status"] = "Planned",
            ["Shipment"] = "SH-0000012",
        }));
        Section(page, "Transport Order").QuerySelector("a[href='/shipments/SH-0000012']").Should().NotBeNull();
    }

    [Fact]
    public void Render_ATransportOrderNotYetPlanned_ShowsNoShipment()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.View("TO-0000042")));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Transport Order").Should().NotContainKey("Shipment"));
    }

    [Fact]
    public void Render_ATransportOrderNotYetRated_SaysSoWithoutAFreightCharge()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.View("TO-0000042")));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Rating").Should().Equal(new Dictionary<string, string>
        {
            ["Rating result"] = "Not yet rated",
        }));
        Section(page, "Rating").QuerySelectorAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_ARatedTransportOrder_ShowsItsFreightChargeByChargeLineWithTheTotalAndRateCardVersion()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.RatedView("TO-0000042")));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Rating").Should().Equal(new Dictionary<string, string>
        {
            ["Rating result"] = "Rated",
            ["Rate Card"] = "RC-0001, version 3",
            ["Total"] = "EUR 65.00",
        }));
        Rows(page, "Rating").Should().Equal("Freight | EUR 38.40", "Minimum charge adjustment | EUR 26.60");
    }

    [Fact]
    public void Render_ATransportOrderWhoseRatingFailed_ShowsWhyWithoutAFreightCharge()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.RatingFailedView("TO-0000042")));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Rating").Should().Equal(new Dictionary<string, string>
        {
            ["Rating result"] = "Rating failed",
            ["Reason"] = "No Lane of Rate Card RC-0001 goes from Rotterdam to DE 40210.",
        }));
        Section(page, "Rating").QuerySelectorAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_AFulfilmentTransportOrder_SaysItIsNotRatedSeparately()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns(Detail(Samples.View("TO-0000042") with { RatingResult = RatingResult.NotRatedSeparately }));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Rating")["Rating result"].Should().Be("Not rated separately"));
    }

    [Fact]
    public void Render_ATransportOrderWithoutTheOptionalParts_SaysTheyWereNotGiven()
    {
        var view = Samples.View("TO-0000042");
        view = view with
        {
            TransportOrder = view.TransportOrder with
            {
                Delivery = view.TransportOrder.Delivery! with { Window = null },
                Instructions = null,
            },
        };
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(view));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Delivery")["Window"].Should().Be("Not given"));
        Section(page, "Instructions").TextContent.Should().Contain("Not given");
    }

    [Fact]
    public void Render_ATransportOrderWithoutGoodsLines_SaysNoneWereGiven()
    {
        var view = Samples.View("TO-0000042");
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns(Detail(view with { TransportOrder = view.TransportOrder with { GoodsLines = [] } }));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Section(page, "Goods").TextContent.Should().Contain("No goods lines were given."));
        Section(page, "Goods").QuerySelectorAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_ATransportOrderNotYetPlanned_SaysThereIsNoShipmentYetAndLinksToThoseWaitingForPlanning()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.View("TO-0000042")));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Section(page, "Shipment").TextContent.Should().Contain("No Shipment yet"));
        Section(page, "Shipment").QuerySelector("a[href='/transport-orders']").Should().NotBeNull();
        Section(page, "Trip").TextContent.Should().Contain("No Trip: there is no Shipment yet.");
    }

    [Fact]
    public void Render_APlannedOwnFleetOrderOnNoTrip_ShowsTheShipmentCardAndSaysItIsOnNoTripLinkingToShipmentsToPutOnTrips()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.View("TO-0000042"), OwnFleetShipment(), null));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Shipment").Should().Equal(new Dictionary<string, string>
        {
            ["Shipment"] = "SH-0000012",
            ["Status"] = "Released",
            ["Carried by"] = "Own fleet",
        }));
        Section(page, "Shipment").QuerySelector("a[href='/shipments/SH-0000012']").Should().NotBeNull();
        Section(page, "Trip").TextContent.Should().Contain("Not on a Trip yet.");
        Section(page, "Trip").QuerySelector("a[href='/trips/shipments']").Should().NotBeNull();
    }

    [Fact]
    public void Render_ACarrierShipment_ShowsWhoCarriesItNowAndItsCarrierBookingStatus()
    {
        var shipment = new TransportOrderShipmentView(
            "SH-0000012", ShipmentStatus.Booked, new CarriedByView(AssignedTo.Carrier, Samples.Kessler), Booking(CarrierBookingStatus.Confirmed));
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.View("TO-0000042"), shipment, null));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Shipment").Should().Equal(new Dictionary<string, string>
        {
            ["Shipment"] = "SH-0000012",
            ["Status"] = "Booked",
            ["Carried by"] = "Kessler Spedition",
            ["Carrier booking"] = "Confirmed",
        }));
        Section(page, "Trip").TextContent.Should().Contain("Not on a Trip: a Carrier carries this Shipment.");
    }

    [Fact]
    public void Render_AShipmentOnATrip_ShowsTheTripAsALinkWithItsDayAndStatusAndThisOrdersStopsWithTheirOutcome()
    {
        var stops = new[]
        {
            new TripStopView(0, StopKind.Collection, "SH-0000012", "TO-0000042", "Waalhaven Oostzijde 88, 3087 BM Rotterdam", null, null, null, "2 pallet",
                StopOutcomeKind.Collected, new DateTimeOffset(2026, 10, 13, 6, 30, 0, TimeSpan.Zero), "Dock 4"),
            new TripStopView(1, StopKind.Delivery, "SH-0000012", "TO-0000042", "Oudegracht 112, 3511 AW Utrecht", "Café De Zwaan", null, null, "2 pallet"),
        };
        var trip = new TransportOrderTripView("TR-0000007", new DateOnly(2026, 10, 13), TripStatus.InProgress, stops);
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Detail(Samples.View("TO-0000042"), OwnFleetShipment(), trip));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Trip").Should().Equal(new Dictionary<string, string>
        {
            ["Trip"] = "TR-0000007",
            ["Day"] = "Tue 13 Oct",
            ["Status"] = "InProgress",
        }));
        Section(page, "Trip").QuerySelector("a[href='/trips/TR-0000007']").Should().NotBeNull();
        Rows(page, "Trip").Should().Equal(
            "1 | Collect | SH-0000012 | Waalhaven Oostzijde 88, 3087 BM Rotterdam | Not given | 2 pallet | Collected · Tue 13 Oct 2026, 08:30 · Dock 4",
            "2 | Deliver | SH-0000012 | Café De Zwaan, Oudegracht 112, 3511 AW Utrecht | Not given | 2 pallet | Open");
    }

    private static TransportOrderDetailView Detail(TransportOrderView view, TransportOrderShipmentView? shipment, TransportOrderTripView? trip) => new(view, shipment, trip);

    private static TransportOrderShipmentView OwnFleetShipment() =>
        new("SH-0000012", ShipmentStatus.Released, new CarriedByView(AssignedTo.OwnFleet, null), null);

    private static CarrierBookingView Booking(CarrierBookingStatus status) => new(
        "CB-0000003", Samples.Kessler, status, new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero), null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Render_BeforeTheTransportOrderIsRead_ShowsLoading()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<TransportOrderDetailView?>().Task);

        var page = RenderDetail("TO-0000042");

        page.Find("[role=status]").TextContent.Should().Contain("Loading Transport Order TO-0000042");
    }

    [Fact]
    public void Render_AnUnknownNumber_SaysThereIsNoSuchTransportOrder()
    {
        Queries.FindAsync("TO-0000999", Arg.Any<CancellationToken>()).Returns((TransportOrderDetailView?)null);

        var page = RenderDetail("TO-0000999");

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("There is no Transport Order TO-0000999."));
        page.FindAll("section").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheTransportOrderCantBeRead_ShowsAnError()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns<TransportOrderDetailView?>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Transport Order TO-0000042 couldn't be loaded. Reload the page to try again."));
    }

    private static TransportOrderDetailView Detail(TransportOrderView view) => new(view, null, null);

    private IRenderedComponent<TransportOrderDetail> RenderDetail(string number) =>
        Render<TransportOrderDetail>(parameters => parameters.Add(page => page.TransportOrderNumber, number));

    private static IElement Section(IRenderedComponent<TransportOrderDetail> page, string heading) =>
        page.FindAll("section").Single(section => section.QuerySelector("h2")?.TextContent.Trim() == heading);

    private static Dictionary<string, string> Definitions(IRenderedComponent<TransportOrderDetail> page, string heading) =>
        Section(page, heading).QuerySelectorAll("dt").ToDictionary(
            term => term.TextContent.Trim(),
            term => term.NextElementSibling!.TextContent.Trim());

    // Each row of the section's table as its cells joined with " | ".
    private static IEnumerable<string> Rows(IRenderedComponent<TransportOrderDetail> page, string heading) =>
        Section(page, heading).QuerySelectorAll("tbody tr")
            .Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));
}
