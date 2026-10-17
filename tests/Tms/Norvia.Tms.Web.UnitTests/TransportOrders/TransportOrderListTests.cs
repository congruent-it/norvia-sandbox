using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Browse;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class TransportOrderListTests : TmsWebContext
{
    private static readonly ShipperView Verhoeven = new("SHP-0002", "Verhoeven Foods");

    public TransportOrderListTests()
    {
        Queries.ListShippersAsync(Arg.Any<CancellationToken>()).Returns([Samples.BrouwerDranken, Verhoeven]);
        Queries.ListTransportOrdersAsync(Arg.Any<TransportOrderStatus?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(PageOf());
    }

    [Fact]
    public void Render_BeforeTheTransportOrdersAreRead_ShowsLoading()
    {
        Queries.ListTransportOrdersAsync(null, null, null, 1, Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<TransportOrderPage>().Task);

        var page = Render<TransportOrderList>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Transport Orders");
    }

    [Fact]
    public void Render_AnAcceptedAndAPlannedOrder_ListsEachWithItsColumnsAndTheShipmentWhenThereIsOne()
    {
        Queries.ListTransportOrdersAsync(null, null, null, 1, Arg.Any<CancellationToken>()).Returns(PageOf(
            Item("TO-0000043", TransportOrderStatus.Planned, "SH-0000012", ShipmentStatus.Released),
            Item("TO-0000042", TransportOrderStatus.Accepted, null, null)));

        var page = Render<TransportOrderList>();

        var planned = page.WaitForElement("tr:has(a[href='/transport-orders/TO-0000043'])");
        Cells(planned).Should().Equal(
            "TO-0000043", "Brouwer Dranken", "BD-2026-0042", "Planned", "Rotterdam, NL", "Utrecht, NL", "Sat 26 Sep 2026, 06:00–10:00", "SH-0000012", "Released");
        planned.QuerySelector("a[href='/shipments/SH-0000012']").Should().NotBeNull();
        Cells(page.Find("tr:has(a[href='/transport-orders/TO-0000042'])")).Should().Equal(
            "TO-0000042", "Brouwer Dranken", "BD-2026-0042", "Accepted", "Rotterdam, NL", "Utrecht, NL", "Sat 26 Sep 2026, 06:00–10:00", "—", "—");
    }

    [Fact]
    public void Render_TheShipperFilter_OffersEveryShipperByName()
    {
        var page = Render<TransportOrderList>();

        page.WaitForElement("fluent-select[aria-label='Shipper'] fluent-option[value='SHP-0002']").TextContent.Trim().Should().Be("Verhoeven Foods");
    }

    [Fact]
    public void ChoosingAStatus_ReadsAgainWithThatStatusFromTheFirstPage()
    {
        var page = Render<TransportOrderList>();

        page.WaitForElement("fluent-select[aria-label='Status']").Change("Planned");

        page.WaitForAssertion(() => Queries.Received().ListTransportOrdersAsync(TransportOrderStatus.Planned, null, null, 1, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ChoosingAShipper_ReadsAgainWithThatShipper()
    {
        var page = Render<TransportOrderList>();

        page.WaitForElement("fluent-select[aria-label='Shipper']").Change("SHP-0002");

        page.WaitForAssertion(() => Queries.Received().ListTransportOrdersAsync(null, "SHP-0002", null, 1, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Searching_ReadsAgainWithTheTrimmedTextAlongsideTheFilters()
    {
        var page = Render<TransportOrderList>();
        page.WaitForElement("fluent-select[aria-label='Status']").Change("Accepted");

        page.WaitForElement("fluent-text-field[aria-label='Search']").Change("  bd-2026  ");

        page.WaitForAssertion(() => Queries.Received().ListTransportOrdersAsync(TransportOrderStatus.Accepted, null, "bd-2026", 1, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Searching_WithNoMatch_ShowsAnEmptyStateNotAnError()
    {
        Queries.ListTransportOrdersAsync(null, null, "nothing", 1, Arg.Any<CancellationToken>()).Returns(PageOf());
        var page = Render<TransportOrderList>();

        page.WaitForElement("fluent-text-field[aria-label='Search']").Change("nothing");

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("No Transport Orders match the search and filters."));
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithNoTransportOrdersAtAll_SaysNoneHaveBeenAcceptedYet()
    {
        var page = Render<TransportOrderList>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("There are no Transport Orders yet."));
    }

    [Fact]
    public void Render_AManyPageResult_ShowsThePositionAndPagesWithNextAndPrevious()
    {
        Queries.ListTransportOrdersAsync(null, null, null, 1, Arg.Any<CancellationToken>())
            .Returns(PageOf(totalCount: 60, page: 1, items: [Item("TO-0000042", TransportOrderStatus.Accepted, null, null)]));
        Queries.ListTransportOrdersAsync(null, null, null, 2, Arg.Any<CancellationToken>())
            .Returns(PageOf(totalCount: 60, page: 2, items: [Item("TO-0000017", TransportOrderStatus.Accepted, null, null)]));
        var page = Render<TransportOrderList>();
        page.WaitForAssertion(() => page.Find("nav[aria-label=Pages] [role=status]").TextContent.Should().Be("Page 1 of 3 · 60 Transport Orders"));
        page.FindAll("nav[aria-label=Pages] fluent-button")[0].HasAttribute("disabled").Should().BeTrue("there is no page before the first");

        page.FindAll("nav[aria-label=Pages] fluent-button")[1].Click();

        page.WaitForElement("a[href='/transport-orders/TO-0000017']");
        page.Find("nav[aria-label=Pages] [role=status]").TextContent.Should().Be("Page 2 of 3 · 60 Transport Orders");
    }

    [Fact]
    public void Render_WhenTheTransportOrdersCantBeRead_ShowsAnError()
    {
        Queries.ListTransportOrdersAsync(null, null, null, 1, Arg.Any<CancellationToken>())
            .Returns<TransportOrderPage>(_ => throw new TimeoutException("The database didn't answer."));

        var page = Render<TransportOrderList>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The Transport Orders couldn't be loaded."));
    }

    [Fact]
    public void Render_WhenTheShippersCantBeRead_StillListsTheTransportOrders()
    {
        Queries.ListShippersAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ShipperView>>(_ => throw new TimeoutException("No Shippers."));
        Queries.ListTransportOrdersAsync(null, null, null, 1, Arg.Any<CancellationToken>())
            .Returns(PageOf(Item("TO-0000042", TransportOrderStatus.Accepted, null, null)));

        var page = Render<TransportOrderList>();

        page.WaitForElement("a[href='/transport-orders/TO-0000042']");
    }

    private static string[] Cells(AngleSharp.Dom.IElement row) => [.. row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())];

    private static TransportOrderPage PageOf(params TransportOrderListItem[] items) => PageOf(items.Length, 1, items);

    private static TransportOrderPage PageOf(int totalCount, int page, TransportOrderListItem[] items) => new(items, totalCount, page, 25);

    // Collection 06:00–10:00 on 26 September 2026 in Rotterdam (UTC+2), from Rotterdam to Utrecht.
    private static TransportOrderListItem Item(string number, TransportOrderStatus status, string? shipmentNumber, ShipmentStatus? shipmentStatus) => new(
        number,
        status,
        Samples.BrouwerDranken,
        "BD-2026-0042",
        new PlaceView("3087 BM", "Rotterdam", "NL"),
        new PlaceView("3511 AW", "Utrecht", "NL"),
        new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
        shipmentNumber,
        shipmentStatus);
}
