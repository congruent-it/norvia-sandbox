using AngleSharp.Dom;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class DeliveredWithoutProofOfDeliveryTests : TmsWebContext
{
    [Fact]
    public void Render_BeforeTheShipmentsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>>();
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<DeliveredWithoutProofOfDelivery>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Shipments delivered without Proof of delivery");
    }

    [Fact]
    public void Render_ADeliveredShipment_ShowsItsCarrierAndDeliveredTimeWithALinkToItsTimeline()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns([Delivered("SH-0000012")]);

        var page = Render<DeliveredWithoutProofOfDelivery>();

        page.WaitForAssertion(() => CellsOf(page, "SH-0000012").Should().Equal("SH-0000012", "Kessler Spedition", "Sat 26 Sep 2026, 12:00", ""));
        page.Find("a[href='/shipments/SH-0000012']");
    }

    [Fact]
    public void Render_AShipmentPastTheMissingThreshold_FlagsItMissing()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>())
            .Returns([Delivered("SH-0000012") with { IsProofOfDeliveryMissing = true }, Delivered("SH-0000013")]);

        var page = Render<DeliveredWithoutProofOfDelivery>();

        page.WaitForAssertion(() =>
        {
            CellsOf(page, "SH-0000012")[3].Should().Be("Missing");
            CellsOf(page, "SH-0000013")[3].Should().BeEmpty();
        });
    }

    [Fact]
    public void Render_Shipments_ListsThemInTheOrderTheQueryGives()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>())
            .Returns([Delivered("SH-0000013") with { IsProofOfDeliveryMissing = true }, Delivered("SH-0000012")]);

        var page = Render<DeliveredWithoutProofOfDelivery>();

        page.WaitForAssertion(() => page.FindAll("a[href^='/shipments/']").Select(link => link.TextContent).Should().Equal("SH-0000013", "SH-0000012"));
    }

    [Fact]
    public void Render_ListsOnlyReadingColumns_NoActionToChangeAShipment()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns([Delivered("SH-0000012")]);

        var page = Render<DeliveredWithoutProofOfDelivery>();

        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.FindAll("fluent-button, button, input").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithNoShipments_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<DeliveredWithoutProofOfDelivery>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("Every Delivered Shipment has its Proof of delivery."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenAProofOfDeliveryArrivesWhileTheListIsOpen_DropsTheShipmentWithoutARefresh()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns(
            [Delivered("SH-0000012"), Delivered("SH-0000013")],
            [Delivered("SH-0000013")]);
        var page = Render<DeliveredWithoutProofOfDelivery>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("a[href='/shipments/SH-0000012']").Should().BeEmpty());
        page.Find("a[href='/shipments/SH-0000013']");
    }

    [Fact]
    public void Render_WhenAShipmentIsDeliveredWhileTheListIsOpen_ShowsItWithoutARefresh()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns([], [Delivered("SH-0000012")]);
        var page = Render<DeliveredWithoutProofOfDelivery>();
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("Every Delivered Shipment"));

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/shipments/SH-0000012']");
    }

    [Fact]
    public void Render_WhenTheShipmentsCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        Queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>>([Delivered("SH-0000012")]));

        var page = Render<DeliveredWithoutProofOfDelivery>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The Shipments delivered without Proof of delivery couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    // Delivered at 12:00 on 26 September 2026 in Rotterdam (UTC+2), carried by Kessler Spedition.
    private static DeliveredShipmentWithoutProofOfDeliverySummary Delivered(string number) => new(
        number,
        new CarriedByView(AssignedTo.Carrier, Samples.Kessler),
        new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero),
        false);

    private static List<string> CellsOf(IRenderedComponent<DeliveredWithoutProofOfDelivery> page, string shipmentNumber) =>
        [.. page.Find($"tr:has(a[href='/shipments/{shipmentNumber}'])").QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())];
}
