using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.ShipperTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperTracking;

public sealed class TrackedOrderTests : PortalWebContext
{
    private IRenderedComponent<TrackedOrder> RenderPage(string number = "TO-0000042") =>
        Render<TrackedOrder>(parameters => parameters.Add(page => page.PersonaId, "brouwer-dranken").Add(page => page.TransportOrderNumber, number));

    private void GetReturns(TrackingAnswer<ShipperTrackingDetail> answer) =>
        Portal.GetTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "TO-0000042", Arg.Any<CancellationToken>()).Returns(answer);

    private int GetCalls => Portal.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPartnerPortal.GetTrackedTransportOrderAsync));

    [Fact]
    public void Render_ShowsTheTransportOrdersPlacesWindowsEtaAndLastUpdate_AsTheApiReturnsThem()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail()));

        var text = RenderPage().Find("dl[aria-label='Transport Order']").TextContent;

        text.Should().Contain("Delivered").And.Contain("BD-2030-0001").And.Contain("Rotterdam, NL").And.Contain("01-10-2030 08:00 to 01-10-2030 12:00")
            .And.Contain("Düsseldorf, DE").And.Contain("02-10-2030 09:00 to 02-10-2030 13:00").And.Contain("02-10-2030 10:30").And.Contain("02-10-2030 10:05");
    }

    [Fact]
    public void Render_ShowsTheProgressInTheOrderTheApiGives_WithTimeAndPlace()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail()));

        var steps = RenderPage().FindAll("ol[aria-label=Progress] li");

        steps.Select(step => step.TextContent).Should().HaveCount(3);
        steps[0].TextContent.Should().Contain("01-10-2030 09:30").And.Contain("Collected").And.Contain("Rotterdam, NL");
        steps[1].TextContent.Should().Contain("Out for delivery").And.Contain("Düsseldorf, DE");
        steps[2].TextContent.Should().Contain("02-10-2030 10:05").And.Contain("Delivered");
    }

    [Fact]
    public void Render_ShowsTheCurrentProofOfDelivery_WithWhoReceivedItAndInWhatCondition()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail()));

        RenderPage().Find("dl[aria-label='Proof of delivery']").TextContent.Should().Contain("M. Müller").And.Contain("02-10-2030 10:05").And.Contain("Delivered with reservations");
    }

    [Fact]
    public void Render_TheProofOfDeliverysFiles_DownloadThroughThePortal()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail()));

        var links = RenderPage().FindAll("ul[aria-label='Proof of delivery files'] a");

        links.Select(link => link.TextContent).Should().Equal("CMR signed.pdf", "damage.jpg");
        links[0].GetAttribute("href").Should().Be($"/personas/brouwer-dranken/tracking/TO-0000042/proof-of-delivery/files/{TrackingSamples.CmrFile}");
        links[0].HasAttribute("download").Should().BeTrue();
        links[0].ParentElement!.TextContent.Should().Contain("47.1 kB");
        links[1].ParentElement!.TextContent.Should().Contain("1.5 MB");
    }

    [Fact]
    public void Render_WithoutAProofOfDelivery_SaysThereIsNoneYet()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail(status: ShipperTrackingStatus.InTransit, withProof: false)));

        var page = RenderPage();

        page.Markup.Should().Contain("There is no Proof of delivery yet.");
        page.FindAll("ul[aria-label='Proof of delivery files']").Should().BeEmpty();
    }

    [Fact]
    public void Render_BeforeCollection_SaysNothingHasHappenedYet_AndDashesTheTimes()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail() with
        {
            Status = ShipperTrackingStatus.Scheduled,
            Eta = null,
            LastUpdate = null,
            DeliverFrom = null,
            DeliverUntil = null,
            Progress = [],
            ProofOfDelivery = null,
        }));

        var page = RenderPage();

        page.Markup.Should().Contain("Nothing has happened to the goods yet.");
        page.Find("dl[aria-label='Transport Order']").TextContent.Should().Contain("Scheduled").And.Contain("–");
    }

    [Fact]
    public void Render_AProofOfDeliveryWithoutFiles_ListsNone()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail() with
        {
            ProofOfDelivery = TrackingSamples.Detail().ProofOfDelivery! with { Files = [] },
        }));

        RenderPage().FindAll("ul[aria-label='Proof of delivery files']").Should().BeEmpty();
    }

    [Fact]
    public void Render_ATransportOrderWithoutAReferenceOrPlaces_ShowsDashes()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail() with { ShipperReference = null, Pickup = new ShipperPlace(null, null) }));

        var values = RenderPage().FindAll("dl[aria-label='Transport Order'] dd").Select(value => value.TextContent.Trim()).ToArray();

        // Shipper reference, then the collection place.
        values[1].Should().Be("–");
        values[2].Should().Be("–");
    }

    [Fact]
    public void Render_ATransportOrderThatIsNotThere_SaysSo()
    {
        GetReturns(TrackingAnswer.NotFound<ShipperTrackingDetail>("Not found."));

        RenderPage().Find("[role=status]").TextContent.Should().Contain("Brouwer Dranken has no Transport Order TO-0000042");
    }

    [Fact]
    public void Render_LinksBackToTheList()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail()));

        RenderPage().Find("a[href='/personas/brouwer-dranken/tracking']").TextContent.Should().Contain("All Transport Orders");
    }

    [Fact]
    public void Poll_AsksAgainAtThePersonasInterval_AndShowsTheProgressThatCameIn()
    {
        GetReturns(TrackingAnswer.Found(TrackingSamples.Detail() with { Progress = [] }));
        Portal.GetTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "TO-0000042", Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Found(TrackingSamples.Detail() with { Progress = [] }), TrackingAnswer.Found(TrackingSamples.Detail()));
        var page = RenderPage();
        page.Markup.Should().Contain("Nothing has happened");

        Time.Advance(TimeSpan.FromSeconds(2));
        GetCalls.Should().Be(1);
        Time.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() => page.FindAll("ol[aria-label=Progress] li").Should().HaveCount(3));
        GetCalls.Should().Be(2);
    }

    [Fact]
    public void Poll_WhenItFails_KeepsTheLastAnswer_WithItsFetchedAtTime_AndShowsTheError()
    {
        Time.SetUtcNow(new DateTimeOffset(2026, 10, 7, 9, 15, 30, TimeSpan.Zero));
        Portal.GetTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "TO-0000042", Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Found(TrackingSamples.Detail()), TrackingAnswer.Unavailable<ShipperTrackingDetail>("Tracking is temporarily unavailable. Try again shortly.", "corr-8"));
        var page = RenderPage();

        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Tracking is temporarily unavailable. Try again shortly.").And.Contain("corr-8").And.Contain("11:15:30"));
        page.Find("dl[aria-label='Transport Order']").TextContent.Should().Contain("Delivered");
        page.FindAll("ol[aria-label=Progress] li").Should().HaveCount(3);
        page.Find("[data-testid=fetched-at]").TextContent.Should().Be("Fetched at 11:15:30");
    }

    [Fact]
    public void Poll_WhenItThrows_KeepsTheLastAnswer_AndShowsAnError()
    {
        Portal.GetTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "TO-0000042", Arg.Any<CancellationToken>())
            .Returns(_ => TrackingAnswer.Found(TrackingSamples.Detail()), _ => throw new InvalidOperationException("Down."));
        var page = RenderPage();

        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Tracking couldn't be fetched."));
        page.FindAll("ol[aria-label=Progress] li").Should().HaveCount(3);
    }

    [Fact]
    public void Poll_WhenTheTransportOrderGoesMissing_SaysSoInsteadOfKeepingIt()
    {
        Portal.GetTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "TO-0000042", Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Found(TrackingSamples.Detail()), TrackingAnswer.NotFound<ShipperTrackingDetail>("Not found."));
        var page = RenderPage();

        Time.Advance(TimeSpan.FromSeconds(3));

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("has no Transport Order"));
    }

    [Fact]
    public void Render_WhenTheFirstAnswerIsAnError_ShowsItWithoutAnAnswer()
    {
        GetReturns(TrackingAnswer.Failed<ShipperTrackingDetail>(TrackingOutcome.Refused, "Norvia didn't recognise this Persona's subscription key or token."));

        var page = RenderPage();

        page.Find("[role=alert]").TextContent.Should().Contain("didn't recognise");
        page.FindAll("dl").Should().BeEmpty();
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

        Render<TrackedOrder>(parameters => parameters.Add(page => page.PersonaId, personaId).Add(page => page.TransportOrderNumber, "TO-0000042"));

        notFound.Should().BeTrue();
    }
}
