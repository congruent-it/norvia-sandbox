using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class WaitingForPlanningTests : TmsWebContext
{
    [Fact]
    public void Render_BeforeTheTransportOrdersAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<TransportOrderSummary>>();
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>())
            .Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<WaitingForPlanning>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Transport Orders waiting for planning");
    }

    [Fact]
    public void Render_WithTransportOrdersWaiting_ListsEachWithNumberShipperReferenceAndCollectionWindowInRotterdamTime()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([Samples.Summary("TO-0000042", "BD-2026-0042")]);

        var page = Render<WaitingForPlanning>();

        var row = page.WaitForElement("tr:has(a[href='/transport-orders/TO-0000042'])");
        var cells = row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim());
        cells.Should().Equal("TO-0000042", "Brouwer Dranken", "BD-2026-0042", "Sat 26 Sep 2026, 06:00–10:00");
    }

    [Fact]
    public void Render_WithNoTransportOrdersWaiting_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<WaitingForPlanning>();

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("No Transport Orders are waiting for planning."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenATransportOrderIsAcceptedWhileTheListIsOpen_ShowsItWithoutARefresh()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            [Samples.Summary("TO-0000042")],
            [Samples.Summary("TO-0000042"), Samples.Summary("TO-0000043", "BD-2026-0043")]);
        var page = Render<WaitingForPlanning>();
        page.WaitForElement("a[href='/transport-orders/TO-0000042']");

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/transport-orders/TO-0000043']");
    }

    [Fact]
    public void Render_WhenTheTransportOrdersCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<TransportOrderSummary>>([Samples.Summary("TO-0000042")]));

        var page = Render<WaitingForPlanning>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The Transport Orders waiting for planning couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/transport-orders/TO-0000042']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsTheTransportOrdersAlreadyShownAndWarnsTheyMayBeOutOfDate()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<IReadOnlyList<TransportOrderSummary>>([Samples.Summary("TO-0000042")]),
            _ => throw new TimeoutException("The database didn't answer."));
        var page = Render<WaitingForPlanning>();
        page.WaitForElement("a[href='/transport-orders/TO-0000042']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The list couldn't be refreshed and may be out of date. Trying again…"));
        page.Find("a[href='/transport-orders/TO-0000042']");
    }
}
