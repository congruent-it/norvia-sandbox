using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

public sealed class ShipmentsTests : PortalWebContext
{
    private static readonly CarrierBookingDetails Details = new(
        new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
        new CarrierBookingStop("Oudegracht 112", "3511 AW", "Utrecht", "NLD", null, null),
        null,
        []);

    private static CarrierShipmentView Shipment(string number = "CB-0000001", TrackingEventType? lastProgress = null) => new(
        number, "SH-0000042", "VDT-77812", Details, TrackingRules.StageOf(lastProgress),
        TrackingRules.NextProgress(lastProgress), TrackingRules.Exceptions(lastProgress));

    private static CarrierTrackingEvent Sent(TrackingEventType type) =>
        new("evt-1", "VDT-77812", "CB-0000001", type, new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero), "Rotterdam", "3087 BM", "NLD");

    private IRenderedComponent<Shipments> RenderShipments(params CarrierShipmentView[] shipments)
    {
        Portal.ListCarrierShipmentsAsync("van-dijk-transport", Arg.Any<CancellationToken>()).Returns(shipments);
        return Render<Shipments>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));
    }

    private static IEnumerable<string> ButtonsOf(IRenderedComponent<Shipments> page) =>
        page.FindAll("tbody tr fluent-button").Select(button => button.TextContent.Trim());

    [Fact]
    public void Render_ListsEachShipmentWithItsAddressesReferenceAndStatus()
    {
        var page = RenderShipments(Shipment("CB-0000001"), Shipment("CB-0000002", TrackingEventType.Departed));

        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("CB-0000001").And.Contain("SH-0000042").And.Contain("VDT-77812").And.Contain("Waalhaven Oostzijde 88, 3087 BM Rotterdam, NLD")
            .And.Contain("Oudegracht 112, 3511 AW Utrecht, NLD").And.Contain("Booked");
        rows[1].TextContent.Should().Contain("In transit");
    }

    [Fact]
    public void Render_ABookedShipment_OffersCollectedAndDelivered_AndTheExceptionsIncludingCollectionFailed()
    {
        var page = RenderShipments(Shipment());

        ButtonsOf(page).Should().Equal(
            "Collected (PU)", "Delivered (POD)", "Collection failed (NPU)", "Delivery failed (NDL)", "Damaged (DMG)", "Send Proof of delivery");
    }

    [Fact]
    public void Render_ACollectedShipment_OffersNoCollectionFailed()
    {
        var page = RenderShipments(Shipment(lastProgress: TrackingEventType.Collected));

        ButtonsOf(page).Should().Equal("Departed (DEP)", "Delivered (POD)", "Delivery failed (NDL)", "Damaged (DMG)", "Send Proof of delivery");
    }

    [Fact]
    public void Render_AnInTransitShipment_OffersTheNextStep()
    {
        var page = RenderShipments(Shipment(lastProgress: TrackingEventType.Arrived));

        ButtonsOf(page).Should().Equal("Out for delivery (OFD)", "Delivered (POD)", "Delivery failed (NDL)", "Damaged (DMG)", "Send Proof of delivery");
    }

    [Fact]
    public void Render_WithNoShipments_SaysSo()
    {
        RenderShipments().Find("[role=status]").TextContent.Should().Contain("has no Shipment");
    }

    [Fact]
    public void Click_SendsThatEventAsThePersona_ForThatShipment_AndShowsTheNextStep()
    {
        var after = Shipment(lastProgress: TrackingEventType.Collected);
        Portal.SendTrackingEventAsync("van-dijk-transport", "CB-0000001", TrackingEventType.Collected, Arg.Any<CancellationToken>())
            .Returns(new SentTrackingEvent(after, Sent(TrackingEventType.Collected), TrackingReply.Accepted("corr-1")));
        var page = RenderShipments(Shipment());

        page.Find("[aria-label=\"Collected (PU) for CB-0000001\"]").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=status]").TextContent.Should().Contain("Norvia accepted Collected (PU) for CB-0000001").And.Contain("corr-1");
            page.Find("tbody tr").TextContent.Should().Contain("Collected");
            ButtonsOf(page).Should().StartWith("Departed (DEP)");
        });
    }

    [Fact]
    public void Click_AnException_IsSent_AndTheShipmentStaysWhereItWas()
    {
        var shipment = Shipment();
        Portal.SendTrackingEventAsync("van-dijk-transport", "CB-0000001", TrackingEventType.CollectionFailed, Arg.Any<CancellationToken>())
            .Returns(new SentTrackingEvent(shipment, Sent(TrackingEventType.CollectionFailed), TrackingReply.Accepted("corr-1")));
        var page = RenderShipments(shipment);

        page.Find("[aria-label=\"Collection failed (NPU) for CB-0000001\"]").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=status]").TextContent.Should().Contain("Norvia accepted Collection failed (NPU)");
            page.Find("tbody tr").TextContent.Should().Contain("Booked");
        });
    }

    [Fact]
    public void Click_Delivered_KeepsTheShipmentOnTheList_ForItsProofOfDelivery()
    {
        var delivered = Shipment(lastProgress: TrackingEventType.Delivered);
        Portal.SendTrackingEventAsync("van-dijk-transport", "CB-0000001", TrackingEventType.Delivered, Arg.Any<CancellationToken>())
            .Returns(new SentTrackingEvent(delivered, Sent(TrackingEventType.Delivered), TrackingReply.Accepted("corr-1")));
        var page = RenderShipments(Shipment("CB-0000001"), Shipment("CB-0000002"));

        page.Find("[aria-label=\"Delivered (POD) for CB-0000001\"]").Click();

        page.WaitForAssertion(() =>
        {
            var rows = page.FindAll("tbody tr");
            rows.Should().HaveCount(2);
            rows[0].TextContent.Should().Contain("Delivered");
            rows[0].QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim()).Should().Equal("Send Proof of delivery");
        });
    }

    [Fact]
    public void Click_NorviaDidNotReceiveIt_WarnsAndKeepsTheButtons()
    {
        Portal.SendTrackingEventAsync("van-dijk-transport", "CB-0000001", TrackingEventType.Collected, Arg.Any<CancellationToken>())
            .Returns(new SentTrackingEvent(Shipment(), Sent(TrackingEventType.Collected), TrackingReply.NotReceived(null)));
        var page = RenderShipments(Shipment());

        page.Find("[aria-label=\"Collected (PU) for CB-0000001\"]").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=alert]").TextContent.Should().Contain("did not receive");
            ButtonsOf(page).Should().StartWith("Collected (PU)");
        });
    }

    [Fact]
    public void Click_WhenThePortalFails_SaysSo()
    {
        Portal.SendTrackingEventAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TrackingEventType>(), Arg.Any<CancellationToken>())
            .Returns<SentTrackingEvent?>(_ => throw new InvalidOperationException("The database is down."));
        var page = RenderShipments(Shipment());

        page.Find("[aria-label=\"Collected (PU) for CB-0000001\"]").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("couldn't be sent or recorded"));
    }

    [Fact]
    public void Render_AsShipmentsAreConfirmed_ShowsThemWithoutAReload()
    {
        Portal.ListCarrierShipmentsAsync("van-dijk-transport", Arg.Any<CancellationToken>()).Returns([], [Shipment("CB-0000001")]);
        var page = Render<Shipments>(parameters => parameters.Add(component => component.PersonaId, "van-dijk-transport"));
        page.Find("[role=status]").TextContent.Should().Contain("has no Shipment");

        Time.Advance(Shipments.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("CB-0000001"));
    }

    [Fact]
    public void Render_WhenTheListCantBeLoaded_SaysSo()
    {
        Portal.ListCarrierShipmentsAsync("van-dijk-transport", Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CarrierShipmentView>>(_ => throw new InvalidOperationException("down"));

        Render<Shipments>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"))
            .Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("brouwer-dranken")]
    public void Render_ForAPersonaThatIsNoCarrier_IsNotFound(string personaId)
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<Shipments>(parameters => parameters.Add(page => page.PersonaId, personaId));

        notFound.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_StopsTheRefreshing()
    {
        Portal.ListCarrierShipmentsAsync("van-dijk-transport", Arg.Any<CancellationToken>()).Returns([]);
        var page = Render<Shipments>(parameters => parameters.Add(component => component.PersonaId, "van-dijk-transport"));

        await page.Instance.DisposeAsync();
        Time.Advance(Shipments.RefreshInterval);

        await Portal.Received(1).ListCarrierShipmentsAsync("van-dijk-transport", Arg.Any<CancellationToken>());
    }
}
