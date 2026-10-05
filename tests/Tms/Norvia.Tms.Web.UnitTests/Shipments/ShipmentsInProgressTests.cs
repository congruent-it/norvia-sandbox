using AngleSharp.Dom;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class ShipmentsInProgressTests : TmsWebContext
{
    [Fact]
    public void Render_BeforeTheShipmentsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<ShipmentInProgressSummary>>();
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<ShipmentsInProgress>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Shipments in progress");
    }

    [Fact]
    public void Render_AShipmentWithATrackingEvent_ShowsItsCarrierStatusLastEventAndEta()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns([Collected("SH-0000012")]);

        var page = Render<ShipmentsInProgress>();

        page.WaitForAssertion(() => CellsOf(page, "SH-0000012").Should().Equal(
            "SH-0000012", "Kessler Spedition", "Collected", "Collected, Sat 26 Sep 2026, 06:00, Rotterdam, 3011 AA, NL", "Sun 27 Sep 2026, 12:00", ""));
        page.Find("a[href='/shipments/SH-0000012']");
    }

    [Fact]
    public void Render_AShipmentNoCarrierHasReportedOn_SaysNoneYetAndNoEta()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>())
            .Returns([Collected("SH-0000012") with { Status = ShipmentStatus.Booked, LastTrackingEvent = null, Eta = null }]);

        var page = Render<ShipmentsInProgress>();

        page.WaitForAssertion(() => CellsOf(page, "SH-0000012").Skip(2).Should().Equal("Booked", "None yet", "Not given", ""));
    }

    [Fact]
    public void Render_AnInTransitShipment_SaysInTransit()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns([Collected("SH-0000012") with { Status = ShipmentStatus.InTransit }]);

        var page = Render<ShipmentsInProgress>();

        page.WaitForAssertion(() => CellsOf(page, "SH-0000012")[2].Should().Be("In transit"));
    }

    [Fact]
    public void Render_AStaleShipment_FlagsItStale()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>())
            .Returns([Collected("SH-0000012") with { IsStale = true }, Collected("SH-0000013")]);

        var page = Render<ShipmentsInProgress>();

        page.WaitForAssertion(() =>
        {
            CellsOf(page, "SH-0000012")[5].Should().Be("Stale");
            CellsOf(page, "SH-0000013")[5].Should().BeEmpty();
        });
    }

    [Fact]
    public void Render_Shipments_ListsThemInTheOrderTheQueryGives()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns([Collected("SH-0000013"), Collected("SH-0000012")]);

        var page = Render<ShipmentsInProgress>();

        page.WaitForAssertion(() => page.FindAll("a[href^='/shipments/']").Select(link => link.TextContent).Should().Equal("SH-0000013", "SH-0000012"));
    }

    [Fact]
    public void Render_ListsOnlyReadingColumns_NoActionToChangeAShipment()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns([Collected("SH-0000012")]);

        var page = Render<ShipmentsInProgress>();

        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.FindAll("fluent-button, button, input").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithNoShipmentsInProgress_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<ShipmentsInProgress>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("No Shipments are in progress."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenATrackingEventArrivesWhileTheListIsOpen_ShowsItWithoutARefresh()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns(
            [Collected("SH-0000012") with { LastTrackingEvent = null, Status = ShipmentStatus.Booked }],
            [Collected("SH-0000012")]);
        var page = Render<ShipmentsInProgress>();
        page.WaitForAssertion(() => CellsOf(page, "SH-0000012")[3].Should().Be("None yet"));

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => CellsOf(page, "SH-0000012")[3].Should().StartWith("Collected,"));
    }

    [Fact]
    public void Render_WhenAShipmentIsDeliveredWhileTheListIsOpen_DropsItWithoutARefresh()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns(
            [Collected("SH-0000012"), Collected("SH-0000013")],
            [Collected("SH-0000013")]);
        var page = Render<ShipmentsInProgress>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("a[href='/shipments/SH-0000012']").Should().BeEmpty());
        page.Find("a[href='/shipments/SH-0000013']");
    }

    [Fact]
    public void Render_WhenTheShipmentsCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        Queries.ListShipmentsInProgressAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<ShipmentInProgressSummary>>([Collected("SH-0000012")]));

        var page = Render<ShipmentsInProgress>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The Shipments in progress couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    // Collected at 06:00 on 26 September 2026 in Rotterdam (UTC+2), carried by Kessler Spedition, expected the next day at 12:00.
    private static ShipmentInProgressSummary Collected(string number) => new(
        number,
        new CarriedByView(AssignedTo.Carrier, Samples.Kessler),
        ShipmentStatus.Collected,
        new LastTrackingEventView(TrackingEventType.Collected, new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero), "Rotterdam", "3011 AA", "NL"),
        new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero),
        false);

    private static List<string> CellsOf(IRenderedComponent<ShipmentsInProgress> page, string shipmentNumber) =>
        [.. page.Find($"tr:has(a[href='/shipments/{shipmentNumber}'])").QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())];
}
