using AngleSharp.Dom;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class TransportOrderDetailTests : TmsWebContext
{
    [Fact]
    public void Render_AnAcceptedTransportOrder_ShowsEverythingTheShipperSubmitted()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Samples.View("TO-0000042"));

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
        GoodsLines(page).Should().Equal("pallet | 2 | 850.5 | 120 × 80 × 150", "crate | 4 | 60 | Not given");
        Section(page, "Instructions").TextContent.Should().Contain("Ring at the side entrance");
    }

    [Fact]
    public void Render_ATransportOrderNotYetRated_SaysSoWithoutAFreightCharge()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Samples.View("TO-0000042"));

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
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Samples.RatedView("TO-0000042"));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Rating").Should().Equal(new Dictionary<string, string>
        {
            ["Rating result"] = "Rated",
            ["Rate Card"] = "RC-0001, version 3",
            ["Total"] = "EUR 65.00",
        }));
        ChargeLines(page).Should().Equal("Freight | EUR 38.40", "Minimum charge adjustment | EUR 26.60");
    }

    [Fact]
    public void Render_ATransportOrderWhoseRatingFailed_ShowsWhyWithoutAFreightCharge()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(Samples.RatingFailedView("TO-0000042"));

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
            .Returns(Samples.View("TO-0000042") with { RatingResult = RatingResult.NotRatedSeparately });

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
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(view);

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Definitions(page, "Delivery")["Window"].Should().Be("Not given"));
        Section(page, "Instructions").TextContent.Should().Contain("Not given");
    }

    [Fact]
    public void Render_ATransportOrderWithoutGoodsLines_SaysNoneWereGiven()
    {
        var view = Samples.View("TO-0000042");
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns(view with { TransportOrder = view.TransportOrder with { GoodsLines = [] } });

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => Section(page, "Goods").TextContent.Should().Contain("No goods lines were given."));
        Section(page, "Goods").QuerySelectorAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_BeforeTheTransportOrderIsRead_ShowsLoading()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<TransportOrderView?>().Task);

        var page = RenderDetail("TO-0000042");

        page.Find("[role=status]").TextContent.Should().Contain("Loading Transport Order TO-0000042");
    }

    [Fact]
    public void Render_AnUnknownNumber_SaysThereIsNoSuchTransportOrder()
    {
        Queries.FindAsync("TO-0000999", Arg.Any<CancellationToken>()).Returns((TransportOrderView?)null);

        var page = RenderDetail("TO-0000999");

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("There is no Transport Order TO-0000999."));
        page.FindAll("section").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheTransportOrderCantBeRead_ShowsAnError()
    {
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>())
            .Returns<TransportOrderView?>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderDetail("TO-0000042");

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Transport Order TO-0000042 couldn't be loaded. Reload the page to try again."));
    }

    private IRenderedComponent<TransportOrderDetail> RenderDetail(string number) =>
        Render<TransportOrderDetail>(parameters => parameters.Add(page => page.TransportOrderNumber, number));

    private static IElement Section(IRenderedComponent<TransportOrderDetail> page, string heading) =>
        page.FindAll("section").Single(section => section.QuerySelector("h2")?.TextContent.Trim() == heading);

    private static Dictionary<string, string> Definitions(IRenderedComponent<TransportOrderDetail> page, string heading) =>
        Section(page, heading).QuerySelectorAll("dt").ToDictionary(
            term => term.TextContent.Trim(),
            term => term.NextElementSibling!.TextContent.Trim());

    // Each goods line as its cells joined with " | ".
    private static IEnumerable<string> GoodsLines(IRenderedComponent<TransportOrderDetail> page) =>
        Section(page, "Goods").QuerySelectorAll("tbody tr")
            .Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));

    // Each charge line as its cells joined with " | ".
    private static IEnumerable<string> ChargeLines(IRenderedComponent<TransportOrderDetail> page) =>
        Section(page, "Rating").QuerySelectorAll("tbody tr")
            .Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));
}
