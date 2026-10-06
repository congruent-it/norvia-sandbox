using AngleSharp.Dom;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class ShipmentDetailTests : TmsWebContext
{
    [Fact]
    public void Render_APlannedShipment_ShowsItsStatusWhoCarriesItAndItsTransportOrder()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(Samples.Shipment("SH-0000012"));

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => page.Find("h1").TextContent.Should().Be("Shipment SH-0000012"));
        Definitions(page, "Shipment").Should().Equal(new Dictionary<string, string>
        {
            ["Status"] = "Planned",
            ["Carried by"] = "Kessler Spedition",
            ["Transport Order"] = "TO-0000042",
            ["Shipper"] = "Brouwer Dranken",
            ["Planned"] = "Thu 24 Sep 2026, 11:45",
            ["Correlation ID"] = "corr-planning",
        });
        Section(page, "Shipment").QuerySelector("a[href='/transport-orders/TO-0000042']").Should().NotBeNull();
    }

    [Fact]
    public void Render_AReleasedShipmentForOwnFleet_SaysSo()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>())
            .Returns(Samples.Shipment("SH-0000012", ShipmentStatus.Released) with { CarriedBy = new CarriedByView(AssignedTo.OwnFleet, null) });

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => Definitions(page, "Shipment").Should().Contain(new Dictionary<string, string>
        {
            ["Status"] = "Released",
            ["Carried by"] = "Own fleet",
        }));
    }

    [Fact]
    public void Render_AReassignedShipment_ShowsItsHistoryOfAssignmentsInOrder()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(Samples.Shipment("SH-0000012"));

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => Rows(page, "Assignments").Should().Equal(
            "Van Dijk Transport | Thu 24 Sep 2026, 11:45 | corr-planning",
            "Kessler Spedition | Thu 24 Sep 2026, 12:00 | corr-reassignment"));
    }

    [Fact]
    public void Render_ARequestedCarrierBooking_ShowsItsNumberStatusRequestedAndNotYetSent()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>())
            .Returns(Samples.Shipment("SH-0000012", carrierBookings: [Booking("CB-0000001", CarrierBookingStatus.Requested)]));

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => Rows(page, "Carrier bookings").Should().Equal("CB-0000001 | Kessler Spedition | Requested | Thu 24 Sep 2026, 11:45 | Not yet sent | "));
    }

    [Fact]
    public void Render_TheCarrierBookingHistory_ShowsEachWithItsSentTimeAndItsReasonOrCarrierBookingReference()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>())
            .Returns(Samples.Shipment("SH-0000012", ShipmentStatus.Released, carrierBookings:
            [
                Booking("CB-0000001", CarrierBookingStatus.Rejected) with
                {
                    SentAt = new DateTimeOffset(2026, 9, 24, 9, 46, 0, TimeSpan.Zero),
                    ReasonCode = CarrierBookingRejectionReason.NoCapacity,
                    ReasonText = "Fully booked until Friday.",
                },
                Booking("CB-0000002", CarrierBookingStatus.Confirmed) with { CarrierBookingReference = "KS-7781" },
            ]));

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => Rows(page, "Carrier bookings").Should().Equal(
            "CB-0000001 | Kessler Spedition | Rejected | Thu 24 Sep 2026, 11:45 | Thu 24 Sep 2026, 11:46 | Fully booked until Friday.",
            "CB-0000002 | Kessler Spedition | Confirmed | Thu 24 Sep 2026, 11:45 | Not yet sent | Carrier booking reference KS-7781"));
    }

    [Fact]
    public void Render_AShipmentWithNoCarrierBooking_ShowsNoCarrierBookingsSection()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(Samples.Shipment("SH-0000012"));

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => page.FindAll("h2").Select(h => h.TextContent.Trim()).Should().NotContain("Carrier bookings"));
    }

    [Fact]
    public void Render_BeforeTheShipmentIsRead_ShowsLoading()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<ShipmentView?>().Task);

        var page = RenderDetail("SH-0000012");

        page.Find("[role=status]").TextContent.Should().Contain("Loading Shipment SH-0000012");
    }

    [Fact]
    public void Render_AnUnknownNumber_SaysThereIsNoSuchShipment()
    {
        Queries.FindShipmentAsync("SH-0000999", Arg.Any<CancellationToken>()).Returns((ShipmentView?)null);

        var page = RenderDetail("SH-0000999");

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("There is no Shipment SH-0000999."));
        page.FindAll("section").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheShipmentCantBeRead_ShowsAnError()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>())
            .Returns<ShipmentView?>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Shipment SH-0000012 couldn't be loaded. Reload the page to try again."));
    }

    [Fact]
    public void Render_AShipment_ShowsItsTrackingTimeline()
    {
        Queries.FindShipmentAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(Samples.Shipment("SH-0000012"));
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], []));

        var page = RenderDetail("SH-0000012");

        page.WaitForAssertion(() => Section(page, "Tracking timeline").TextContent.Should().Contain("No Tracking events have been reported"));
    }

    private static CarrierBookingView Booking(string number, CarrierBookingStatus status) =>
        new(number, Samples.Kessler, status, new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero), null, null, null, null, null, null, null, null, null);

    private IRenderedComponent<ShipmentDetail> RenderDetail(string number) =>
        Render<ShipmentDetail>(parameters => parameters.Add(page => page.ShipmentNumber, number));

    private static IElement Section(IRenderedComponent<ShipmentDetail> page, string heading) =>
        page.FindAll("section").Single(section => section.QuerySelector("h2")?.TextContent.Trim() == heading);

    private static Dictionary<string, string> Definitions(IRenderedComponent<ShipmentDetail> page, string heading) =>
        Section(page, heading).QuerySelectorAll("dt").ToDictionary(
            term => term.TextContent.Trim(),
            term => term.NextElementSibling!.TextContent.Trim());

    // Each row of the section's table as its cells joined with " | ".
    private static IEnumerable<string> Rows(IRenderedComponent<ShipmentDetail> page, string heading) =>
        Section(page, heading).QuerySelectorAll("tbody tr")
            .Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));
}
