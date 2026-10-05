using AngleSharp.Dom;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class TrackingTimelineTests : TmsWebContext
{
    [Fact]
    public void Render_AShipmentWithTrackingEvents_ShowsTheTimelineInTheOrderTheQueryGives()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(
        [
            Entry(TrackingEventType.Collected, new DateTimeOffset(2026, 9, 26, 6, 0, 0, TimeSpan.Zero), moved: true),
            Entry(TrackingEventType.Damaged, new DateTimeOffset(2026, 9, 26, 7, 0, 0, TimeSpan.Zero), moved: false) with { Remark = "Pallet dented" },
        ]);

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => Rows(page).Should().Equal(
            "Collected | Sat 26 Sep 2026, 08:00 | Rotterdam, 3011 AA, NL | Sat 26 Sep 2026, 08:05 | Yes | Not given | ",
            "Damaged | Sat 26 Sep 2026, 09:00 | Rotterdam, 3011 AA, NL | Sat 26 Sep 2026, 09:05 | No | Not given | Pallet dented"));
    }

    [Fact]
    public void Render_AShipmentNoCarrierHasReportedOn_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns([]);

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => Section(page).TextContent.Should().Contain("No Tracking events have been reported for this Shipment."));
        Section(page).QuerySelectorAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheTimelineCantBeRead_ShowsAnErrorInItsSection()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns<IReadOnlyList<TrackingTimelineEntry>?>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => Section(page).TextContent.Should().Contain("The Tracking timeline couldn't be loaded."));
    }

    // Reported at 08:00 and 09:00 in Rotterdam (UTC+2), received five minutes later.
    private static TrackingTimelineEntry Entry(TrackingEventType type, DateTimeOffset eventTime, bool moved) =>
        new(type, eventTime, "Rotterdam", "3011 AA", "NL", null, null, eventTime.AddMinutes(5), moved);

    private IRenderedComponent<TrackingTimeline> RenderTimeline(string number) =>
        Render<TrackingTimeline>(parameters => parameters.Add(page => page.ShipmentNumber, number));

    private static IElement Section(IRenderedComponent<TrackingTimeline> page) => page.Find("section");

    // Each row of the table as its cells joined with " | ".
    private static IEnumerable<string> Rows(IRenderedComponent<TrackingTimeline> page) =>
        page.FindAll("tbody tr").Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));
}
