using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.ShipperTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperTracking;

public sealed class TrackedOrdersTests : PortalWebContext
{
    private static TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>> Listed(params ShipperTrackingSummary[] orders) => TrackingAnswer.Found<IReadOnlyList<ShipperTrackingSummary>>(orders);

    private void ListReturns(params ShipperTrackingSummary[] orders) =>
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>()).Returns(Listed(orders));

    private IRenderedComponent<TrackedOrders> RenderPage() =>
        Render<TrackedOrders>(parameters => parameters.Add(page => page.PersonaId, "brouwer-dranken"));

    private int ListCalls => Portal.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPartnerPortal.ListTrackedTransportOrdersAsync));

    [Fact]
    public void Render_ListsEachTransportOrder_WithItsTrackingStatusEtaAndLastUpdate()
    {
        ListReturns(TrackingSamples.Summary());

        var row = RenderPage().FindAll("tbody tr").Should().ContainSingle().Subject;

        row.TextContent.Should().Contain("TO-0000042").And.Contain("BD-2030-0001").And.Contain("Rotterdam, NL").And.Contain("Düsseldorf, DE")
            .And.Contain("In transit").And.Contain("03-10-2030 14:30").And.Contain("02-10-2030 09:10");
    }

    [Fact]
    public void Render_ATransportOrderNumber_LinksToItsDetail()
    {
        ListReturns(TrackingSamples.Summary());

        RenderPage().Find("tbody tr a").GetAttribute("href").Should().Be("/personas/brouwer-dranken/tracking/TO-0000042");
    }

    [Fact]
    public void Render_ATransportOrderNotYetCollected_ShowsADashForTheLastUpdate()
    {
        ListReturns(TrackingSamples.Summary(status: ShipperTrackingStatus.Scheduled, eta: null, lastUpdate: null));

        RenderPage().Find("tbody tr").TextContent.Should().Contain("Scheduled").And.Contain("–");
    }

    [Fact]
    public void Render_ShowsTheTimesAsTheApiReturnsThem_AndSaysTheyAreLocal()
    {
        ListReturns(TrackingSamples.Summary(eta: "31-03-2030 04:00"));

        var page = RenderPage();

        page.Find("tbody tr").TextContent.Should().Contain("31-03-2030 04:00");
        page.Markup.Should().Contain("Times are local to the place they are about");
    }

    [Fact]
    public void Render_AsksForTheFirstPageWithNoFilter_AsThePersona()
    {
        ListReturns();

        RenderPage();

        Portal.Received(1).ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_WithNoTransportOrders_SaysSo()
    {
        ListReturns();

        RenderPage().Find("[role=status]").TextContent.Should().Contain("no Transport Orders");
    }

    [Fact]
    public void Filter_ByTrackingStatus_AsksForThatStatus()
    {
        ListReturns();
        var page = RenderPage();

        page.Find("#tracking-status").Change("InTransit");

        Portal.Received().ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(ShipperTrackingStatus.InTransit), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Filter_ByCollectionDates_AsksForThatRange()
    {
        ListReturns();
        var page = RenderPage();

        page.Find("#tracking-from").Change("2030-10-01");
        page.Find("#tracking-to").Change("2030-10-31");

        Portal.Received().ListTrackedTransportOrdersAsync(
            new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(null, new DateOnly(2030, 10, 1), new DateOnly(2030, 10, 31)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Filter_BackToAll_AsksForEveryStatusAgain()
    {
        ListReturns();
        var page = RenderPage();
        page.Find("#tracking-status").Change("Delivered");

        page.Find("#tracking-status").Change(string.Empty);

        Portal.Received().ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Is<ShipperTrackingFilter>(filter => filter.Status == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Filter_Changed_GoesBackToTheFirstPage()
    {
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(Listed([.. Enumerable.Range(1, ShipperTrackingFilter.PageSize).Select(index => TrackingSamples.Summary($"TO-{index:0000000}"))]));
        var page = RenderPage();
        page.FindAll("fluent-button").Single(button => button.TextContent == "Next").Click();

        page.Find("#tracking-status").Change("Collected");

        Portal.Received().ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(ShipperTrackingStatus.Collected, null, null, Page: 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Paging_AFullPage_OffersTheNextOne_AndThePreviousOnceThere()
    {
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(Listed([.. Enumerable.Range(1, ShipperTrackingFilter.PageSize).Select(index => TrackingSamples.Summary($"TO-{index:0000000}"))]));
        var page = RenderPage();

        page.FindAll("fluent-button").Single(button => button.TextContent == "Next").Click();

        Portal.Received().ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(Page: 2), Arg.Any<CancellationToken>());
        page.Find("nav[aria-label=Pages]").TextContent.Should().Contain("Page 2");

        page.FindAll("fluent-button").Single(button => button.TextContent == "Previous").Click();
        Portal.Received().ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(Page: 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Paging_APartPage_OffersNoPaging()
    {
        ListReturns(TrackingSamples.Summary());

        RenderPage().FindAll("nav[aria-label=Pages]").Should().BeEmpty();
    }

    [Fact]
    public void Paging_PastTheLastPage_SaysThereAreNoMore()
    {
        var full = Listed([.. Enumerable.Range(1, ShipperTrackingFilter.PageSize).Select(index => TrackingSamples.Summary($"TO-{index:0000000}"))]);
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(), Arg.Any<CancellationToken>()).Returns(full);
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(Page: 2), Arg.Any<CancellationToken>()).Returns(Listed());
        var page = RenderPage();

        page.FindAll("fluent-button").Single(button => button.TextContent == "Next").Click();

        page.Find("[role=status]").TextContent.Should().Contain("no more Transport Orders");
    }

    [Fact]
    public void Search_ByTransportOrderNumber_OpensItsDetail()
    {
        ListReturns();
        var page = RenderPage();

        Search(page, "to-0000042");

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/personas/brouwer-dranken/tracking/TO-0000042");
        Portal.DidNotReceiveWithAnyArgs().FindTrackedTransportOrderAsync(default!, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Search_ByShipperReference_FindsItAndOpensItsDetail()
    {
        ListReturns();
        Portal.FindTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "BD-2030-0001", Arg.Any<CancellationToken>()).Returns(TrackingAnswer.Found(TrackingSamples.Detail("TO-0000077")));
        var page = RenderPage();

        Search(page, " BD-2030-0001 ");

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/personas/brouwer-dranken/tracking/TO-0000077");
    }

    [Fact]
    public void Search_ForAReferenceNoTransportOrderHas_SaysSo()
    {
        ListReturns();
        Portal.FindTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "nope", Arg.Any<CancellationToken>()).Returns(TrackingAnswer.NotFound<ShipperTrackingDetail>("Not found."));
        var page = RenderPage();

        Search(page, "nope");

        page.Find("[role=status]").TextContent.Should().Contain("No Transport Order has the number or Shipper reference 'nope'");
    }

    [Fact]
    public void Search_WhenTrackingIsUnavailable_ShowsTheError()
    {
        ListReturns();
        Portal.FindTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "BD-1", Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Unavailable<ShipperTrackingDetail>("Tracking is temporarily unavailable."));
        var page = RenderPage();

        Search(page, "BD-1");

        page.Find("[role=status]").TextContent.Should().Contain("Tracking is temporarily unavailable.");
    }

    [Fact]
    public void Search_WhenTheSearchThrows_SaysItCouldntBeMade()
    {
        ListReturns();
        Portal.FindTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "BD-1", Arg.Any<CancellationToken>()).Returns<TrackingAnswer<ShipperTrackingDetail>>(_ => throw new InvalidOperationException("Down."));
        var page = RenderPage();

        Search(page, "BD-1");

        page.Find("[role=status]").TextContent.Should().Contain("The search couldn't be made.");
    }

    [Fact]
    public void Search_Empty_DoesNothing()
    {
        ListReturns();
        var page = RenderPage();

        Search(page, "   ");

        Portal.DidNotReceiveWithAnyArgs().FindTrackedTransportOrderAsync(default!, default!, Xunit.TestContext.Current.CancellationToken);
        page.Markup.Should().NotContain("No Transport Order has");
    }

    [Fact]
    public void Poll_AsksAgainAtThePersonasInterval_AndShowsWhatChanged()
    {
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(Listed(TrackingSamples.Summary(status: ShipperTrackingStatus.Collected)), Listed(TrackingSamples.Summary(status: ShipperTrackingStatus.InTransit)));
        var page = RenderPage();
        page.Find("tbody tr").TextContent.Should().Contain("Collected");

        Time.Advance(TimeSpan.FromSeconds(2));
        ListCalls.Should().Be(1);
        Time.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() => page.Find("tbody tr").TextContent.Should().Contain("In transit"));
        ListCalls.Should().Be(2);
    }

    [Fact]
    public void Poll_KeepsPolling_AfterEachAnswer()
    {
        ListReturns(TrackingSamples.Summary());
        var page = RenderPage();

        Time.Advance(TimeSpan.FromSeconds(3));
        page.WaitForAssertion(() => ListCalls.Should().Be(2));
        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => ListCalls.Should().Be(3));
    }

    [Fact]
    public void Poll_ShowsWhenTheLastAnswerWasFetched()
    {
        ListReturns(TrackingSamples.Summary());
        Time.SetUtcNow(new DateTimeOffset(2026, 10, 7, 9, 15, 30, TimeSpan.Zero));

        var page = RenderPage();

        page.Find("[data-testid=fetched-at]").TextContent.Should().Be("Fetched at 11:15:30");
    }

    [Fact]
    public void Poll_WhenItFails_KeepsTheLastAnswer_WithItsFetchedAtTime_AndShowsTheError()
    {
        Time.SetUtcNow(new DateTimeOffset(2026, 10, 7, 9, 15, 30, TimeSpan.Zero));
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(Listed(TrackingSamples.Summary()), TrackingAnswer.Unavailable<IReadOnlyList<ShipperTrackingSummary>>("Tracking is temporarily unavailable. Try again shortly.", "corr-7"));
        var page = RenderPage();

        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Tracking is temporarily unavailable. Try again shortly.").And.Contain("corr-7").And.Contain("11:15:30"));
        page.Find("tbody tr").TextContent.Should().Contain("TO-0000042");
        page.Find("[data-testid=fetched-at]").TextContent.Should().Be("Fetched at 11:15:30");
    }

    [Fact]
    public void Poll_WhenItThrows_KeepsTheLastAnswer_AndShowsAnError()
    {
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Listed(TrackingSamples.Summary()), _ => throw new InvalidOperationException("Down."));
        var page = RenderPage();

        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Tracking couldn't be fetched."));
        page.Find("tbody tr").TextContent.Should().Contain("TO-0000042");
    }

    [Fact]
    public void Poll_WhenItWorksAgain_TakesTheErrorAway()
    {
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(Listed(TrackingSamples.Summary()), TrackingAnswer.Unavailable<IReadOnlyList<ShipperTrackingSummary>>("Down."), Listed(TrackingSamples.Summary()));
        var page = RenderPage();
        Time.Advance(TimeSpan.FromSeconds(3));
        page.WaitForAssertion(() => page.FindAll("[role=alert]").Should().ContainSingle());

        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => page.FindAll("[role=alert]").Should().BeEmpty());
    }

    [Fact]
    public void Poll_FiltersStayWhenThePollAsksAgain()
    {
        ListReturns();
        var page = RenderPage();
        page.Find("#tracking-status").Change("Delivered");

        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => Portal.Received(2).ListTrackedTransportOrdersAsync(
            new PersonaId("brouwer-dranken"), new ShipperTrackingFilter(ShipperTrackingStatus.Delivered), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Render_WhenTheFirstAnswerIsAnError_ShowsItWithoutAnAnswer()
    {
        Portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Unavailable<IReadOnlyList<ShipperTrackingSummary>>("Tracking is temporarily unavailable."));

        var page = RenderPage();

        page.Find("[role=alert]").TextContent.Should().Contain("Tracking is temporarily unavailable.");
        page.FindAll("table, [role=grid]").Should().BeEmpty();
        page.FindAll("[data-testid=fetched-at]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenThePageCantBeLoaded_SaysSo()
    {
        Portal.ListPersonasAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Norvia.PartnerPortal.Application.Personas.PersonaView>>(_ => throw new InvalidOperationException("Down."));

        RenderPage().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("van-dijk-transport")]
    public void Render_ForAPersonaThatIsNoShipper_IsNotFound(string personaId)
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<TrackedOrders>(parameters => parameters.Add(page => page.PersonaId, personaId));

        notFound.Should().BeTrue();
    }

    [Fact]
    public void Render_ForAShipperWithoutAnInterval_PollsEveryFiveSeconds()
    {
        Portal.ListPersonasAsync(Arg.Any<CancellationToken>()).Returns([new Norvia.PartnerPortal.Application.Personas.PersonaView("brouwer-dranken", "Brouwer Dranken", Norvia.PartnerPortal.Domain.Personas.PartyType.Shipper)]);
        ListReturns(TrackingSamples.Summary());
        var page = RenderPage();

        Time.Advance(TimeSpan.FromSeconds(4));
        ListCalls.Should().Be(1);
        Time.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() => ListCalls.Should().Be(2));
        page.Markup.Should().Contain("every 5 seconds");
    }

    [Fact]
    public async Task Dispose_StopsPolling()
    {
        ListReturns(TrackingSamples.Summary());
        RenderPage();

        await DisposeComponentsAsync();
        Time.Advance(TimeSpan.FromSeconds(10));

        ListCalls.Should().Be(1);
    }

    private static void Search(IRenderedComponent<TrackedOrders> page, string term)
    {
        page.Find("#tracking-search").Input(term);
        page.Find("form[role=search]").Submit();
    }
}
