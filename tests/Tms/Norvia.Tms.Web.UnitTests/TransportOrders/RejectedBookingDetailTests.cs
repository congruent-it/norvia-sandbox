using AngleSharp.Dom;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class RejectedBookingDetailTests : TmsWebContext
{
    [Fact]
    public void Render_ARejectedBooking_ShowsEveryReasonInPlainLanguage()
    {
        var booking = Samples.RejectedView();
        Queries.FindRejectedAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var page = RenderDetail(booking.Id);

        page.WaitForAssertion(() => page.Find("h1").TextContent.Should().Be("Rejected booking BD-2026-0099"));
        Section(page, "Reasons").QuerySelectorAll("li").Select(reason => reason.TextContent.Trim()).Should().Equal(
            "The booking has no goods lines.",
            "The delivery window ends before the collection window starts.");
    }

    [Fact]
    public void Render_ARejectedBooking_ShowsWhoSentItAndWhen()
    {
        var booking = Samples.RejectedView();
        Queries.FindRejectedAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var page = RenderDetail(booking.Id);

        page.WaitForAssertion(() => Definitions(page, "Rejected booking").Should().Equal(new Dictionary<string, string>
        {
            ["Shipper"] = "Brouwer Dranken",
            ["Shipper reference"] = "BD-2026-0099",
            ["Received"] = "Thu 24 Sep 2026, 11:30",
            ["Correlation ID"] = "corr-0099",
        }));
    }

    [Fact]
    public void Render_ARejectedBooking_ShowsEverythingTheShipperSubmitted()
    {
        var booking = Samples.RejectedView();
        Queries.FindRejectedAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var page = RenderDetail(booking.Id);

        page.WaitForAssertion(() => Definitions(page, "Collection")["Address"].Should().Be("Waalhaven Oostzijde 88, 3087 BM Rotterdam, NL"));
        Definitions(page, "Delivery")["Consignee"].Should().Be("Café De Zwaan");
        Definitions(page, "Delivery")["Window"].Should().Be("Sat 26 Sep 2026, 12:00–16:00");
        Section(page, "Goods").TextContent.Should().Contain("No goods lines were given.");
        Section(page, "Instructions").TextContent.Should().Contain("Ring at the side entrance");
    }

    [Fact]
    public void Render_ARejectedBookingWithoutAShipperReference_IsNamedByItsShipper()
    {
        var booking = Samples.RejectedView();
        booking = booking with { TransportOrder = booking.TransportOrder with { ShipperReference = null } };
        Queries.FindRejectedAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var page = RenderDetail(booking.Id);

        page.WaitForAssertion(() => page.Find("h1").TextContent.Should().Be("Rejected booking from Brouwer Dranken"));
        Definitions(page, "Rejected booking")["Shipper reference"].Should().Be("Not given");
    }

    [Fact]
    public void Render_BeforeTheRejectedBookingIsRead_ShowsLoading()
    {
        var id = Guid.NewGuid();
        Queries.FindRejectedAsync(id, Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<TransportOrderView?>().Task);

        var page = RenderDetail(id);

        page.Find("[role=status]").TextContent.Should().Contain("Loading the rejected booking");
    }

    [Fact]
    public void Render_AnUnknownId_SaysThereIsNoSuchRejectedBooking()
    {
        var id = Guid.NewGuid();
        Queries.FindRejectedAsync(id, Arg.Any<CancellationToken>()).Returns((TransportOrderView?)null);

        var page = RenderDetail(id);

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("There is no such rejected booking."));
        page.FindAll("section").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheRejectedBookingCantBeRead_ShowsAnError()
    {
        var id = Guid.NewGuid();
        Queries.FindRejectedAsync(id, Arg.Any<CancellationToken>())
            .Returns<TransportOrderView?>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderDetail(id);

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The rejected booking couldn't be loaded. Reload the page to try again."));
    }

    private IRenderedComponent<RejectedBookingDetail> RenderDetail(Guid id) =>
        Render<RejectedBookingDetail>(parameters => parameters.Add(page => page.Id, id));

    private static IElement Section(IRenderedComponent<RejectedBookingDetail> page, string heading) =>
        page.FindAll("section").Single(section => section.QuerySelector("h2")?.TextContent.Trim() == heading);

    private static Dictionary<string, string> Definitions(IRenderedComponent<RejectedBookingDetail> page, string heading) =>
        Section(page, heading).QuerySelectorAll("dt").ToDictionary(
            term => term.TextContent.Trim(),
            term => term.NextElementSibling!.TextContent.Trim());
}
