using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.ConsigneeTracking;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.ConsigneeTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ConsigneeTracking;

/// <summary>The Consignee's tracking page shows the Consignee's view and nothing of Norvia's workings (ADR 0052).</summary>
public sealed class ConsigneeTrackingPageTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly ConsigneeTrackingView View = new(
        ShipperTrackingStatus.InTransit,
        new ShipperPlace("Rotterdam", "NL"),
        new ShipperPlace("Utrecht", "NL"),
        Now.AddHours(4),
        Now.AddHours(8),
        Now.AddHours(6),
        Now.AddHours(-1),
        [new ConsigneeProgressEvent(ShipperProgressType.Collected, Now.AddHours(-3), new ShipperPlace("Rotterdam", "NL"))],
        new ConsigneeProofOfDelivery("J. Becker", Now.AddHours(-1), ProofOfDeliveryCondition.Reservations));

    [Fact]
    public void Render_AFoundView_ShowsStatusPlacesEtaProgressAndProof()
    {
        var page = Render(TrackingAnswer.Found(View));

        page.Markup.Should().Contain("In transit").And.Contain("Rotterdam, NL").And.Contain("Utrecht, NL");
        page.Markup.Should().Contain("Collected").And.Contain("J. Becker").And.Contain("Delivered with reservations");
        page.Find("[data-testid=fetched-at]").TextContent.Should().Contain("Fetched at");
    }

    [Fact]
    public void Render_TheLastUpdate_IsShownAsOnTheShippersScreen_AndADashBeforeCollection()
    {
        Render(TrackingAnswer.Found(View)).Markup.Should().Contain("Last update").And.Contain(PortalTime.Format(View.LastUpdate!.Value));

        var received = View with { Status = ShipperTrackingStatus.Received, LastUpdate = null, Eta = null, Progress = [], ProofOfDelivery = null };
        var markup = Render(TrackingAnswer.Found(received)).Markup;
        markup.Should().Contain("Nothing has happened to the goods yet.").And.Contain("There is no Proof of delivery yet.");
        markup.Should().MatchRegex("Last update</dt>\\s*<dd>–</dd>");
    }

    [Fact]
    public void Render_IsADocumentOfItsOwn_WithNoScriptAndNoStylesheetToFetch_AndTheRefererSuppressed()
    {
        var markup = Render(TrackingAnswer.Found(View)).Markup;

        markup.Should().NotContain("<script").And.NotContain("rel=\"stylesheet\"").And.NotContain("Persona");
        markup.Should().Contain("name=\"referrer\" content=\"no-referrer\"").And.Contain("noindex");
    }

    [Fact]
    public void Render_AWrongCode_SaysItCantFindTheShipment()
    {
        var page = Render(TrackingAnswer.NotFound<ConsigneeTrackingView>("anything Norvia said"));

        page.Find("[data-testid=tracking-message]").TextContent.Should().Be("We can't find this shipment. Check the link, or contact the sender.");
        page.Markup.Should().NotContain("anything Norvia said");
    }

    [Theory]
    [InlineData(TrackingOutcome.Unavailable, "Tracking is temporarily unavailable. Try again shortly.")]
    [InlineData(TrackingOutcome.Refused, "You have asked a lot of times. Try again in a minute.")]
    public void Render_AFailure_SaysSo(TrackingOutcome outcome, string expected)
    {
        var page = Render(TrackingAnswer.Failed<ConsigneeTrackingView>(outcome, "text"));

        page.Find("[data-testid=tracking-message]").TextContent.Should().Be(expected);
    }

    [Fact]
    public void Render_NeverShowsAnythingNorviaKeepsToItself()
    {
        var markup = Render(TrackingAnswer.Found(View)).Markup;

        foreach (var forbidden in new[] { "Carrier", "Shipment number", "Shipper", "Exception", "Stale", "remark", "price", "TO-0" })
        {
            markup.Should().NotContainEquivalentOf(forbidden);
        }
    }

    private IRenderedComponent<ConsigneeTrackingPage> Render(TrackingAnswer<ConsigneeTrackingView> answer) =>
        Render<ConsigneeTrackingPage>(parameters => parameters.Add(page => page.Answer, answer).Add(page => page.FetchedAt, Now));
}
