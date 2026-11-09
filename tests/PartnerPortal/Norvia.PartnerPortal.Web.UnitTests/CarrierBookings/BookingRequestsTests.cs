using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

public sealed class BookingRequestsTests : PortalWebContext
{
    private static CarrierBookingRequestView Request(string number = "CB-0000001") => new(
        Guid.CreateVersion7(), number, "SH-0000042", new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
        new CarrierBookingDetails(
            new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", new DateTime(2030, 10, 2, 7, 30, 0), new DateTime(2030, 10, 2, 11, 0, 0)),
            new CarrierBookingStop("Kölner Landstraße 301", "40591", "Düsseldorf", "DEU", new DateTime(2030, 10, 3, 8, 0, 0), null),
            "Getränke Schmitz GmbH",
            [
                new CarrierBookingGoodsLine("pallet", 4, 2400m, 120m, 80m, 160m),
                new CarrierBookingGoodsLine("parcel", 3, 12.5m, null, null, null),
            ]));

    [Fact]
    public void Render_ListsEveryRequestWithWhatItAsks()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([Request()]);

        var row = RenderPage().FindAll("tbody tr").Should().ContainSingle().Subject;

        row.TextContent.Should().Contain("Fri 2 Oct 2026, 11:00").And.Contain("CB-0000001").And.Contain("SH-0000042")
            .And.Contain("Waalhaven Oostzijde 88, 3087 BM Rotterdam, NLD").And.Contain("Wed 2 Oct 2030, 07:30");
        row.TextContent.Should().Contain("Kölner Landstraße 301, 40591 Düsseldorf, DEU").And.Contain("Getränke Schmitz GmbH")
            .And.Contain("4 × pallet, 2400 kg, 120 × 80 × 160 cm").And.Contain("3 × parcel, 12.5 kg");
    }

    [Fact]
    public void Render_ShowsTheCarrierCostWithItsChargeLines_AndNeverAFreightCharge()
    {
        var request = Request();
        request = request with
        {
            Details = request.Details with
            {
                CarrierCost = new CarrierCost("EUR", 412.50m, [new CarrierChargeLine("Freight", 400m), new CarrierChargeLine("MinimumChargeAdjustment", 12.50m)], IsSpot: false),
            },
        };
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([request]);

        var text = RenderPage().Find("tbody tr").TextContent;

        text.Should().Contain("412.50 EUR").And.Contain("Freight 400.00").And.Contain("Minimum charge adjustment 12.50")
            .And.NotContain("Spot");
    }

    [Fact]
    public void Render_AnOpenRequestWithACarrierCost_SaysNextToConfirmThatConfirmingAcceptsIt()
    {
        var request = Request();
        request = request with { Details = request.Details with { CarrierCost = new CarrierCost("EUR", 380m, [new CarrierChargeLine("Freight", 380m)], IsSpot: true) } };
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([request]);

        var answer = RenderPage().Find("[aria-label=\"Confirm CB-0000001\"]").ParentElement!.ParentElement!;

        answer.TextContent.Should().Contain("Confirming accepts 380.00 EUR (Spot price).");
    }

    [Fact]
    public void Render_ASpotCarrierCost_SaysItIsASpotPrice()
    {
        var request = Request();
        request = request with { Details = request.Details with { CarrierCost = new CarrierCost("EUR", 380m, [new CarrierChargeLine("Freight", 380m)], IsSpot: true) } };
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([request]);

        RenderPage().Find("tbody tr").TextContent.Should().Contain("380.00 EUR").And.Contain("Spot price");
    }

    [Fact]
    public void Render_AChargeLineWithoutAType_ShowsJustItsAmount()
    {
        var request = Request();
        request = request with { Details = request.Details with { CarrierCost = new CarrierCost("EUR", 5m, [new CarrierChargeLine("", 5m)], IsSpot: false) } };
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([request]);

        RenderPage().Find("tbody tr").TextContent.Should().Contain("5.00 EUR");
    }

    [Fact]
    public void Render_ARequestWithoutACarrierCost_ShowsNoPrice()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([Request()]);

        RenderPage().Find("tbody tr").TextContent.Should().NotContain("EUR");
    }

    [Fact]
    public void Render_ARequestWithAnAutomaticAnswerComingUp_SaysWhen()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns(
            [Request() with { AutoAnswerMode = CarrierAnswerMode.AutoConfirm, AutoAnswerDueAt = new DateTimeOffset(2026, 10, 2, 9, 0, 5, TimeSpan.Zero) }]);

        RenderPage().Find("tbody tr").TextContent.Should().Contain("Auto-confirms at 11:00:05");
    }

    [Fact]
    public void Render_ShowsTheCarriersAnswerMode_EvenWithNoRequests()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([]);

        RenderPage().Find("[aria-label='Answer mode']").TextContent.Should().Contain("Save answer mode");
    }

    [Fact]
    public void Render_ForACarrier_ShowsTheTrackingModeNextToTheAnswerMode()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([]);

        RenderPage().Find("[aria-label='Tracking mode']").TextContent.Should().Contain("Save tracking mode");
    }

    [Fact]
    public void Render_AsRequestsArrive_ShowsThemWithoutAReload()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([], [Request("CB-0000001")]);
        var page = RenderPage();
        page.Find("[role=status]").TextContent.Should().Contain("hasn't asked");

        Time.Advance(BookingRequests.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("CB-0000001"));
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsWhatItShowed_AndTriesAgain()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns(
            _ => [Request("CB-0000001")],
            _ => throw new InvalidOperationException("The database is down."),
            _ => [Request("CB-0000001"), Request("CB-0000002")]);
        var page = RenderPage();

        Time.Advance(BookingRequests.RefreshInterval);
        // Ticks that come while a refresh is still running are one tick, so the next one waits for the failed read.
        page.WaitForAssertion(() => Portal.Received(2).ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()));
        page.FindAll("tbody tr").Should().ContainSingle();
        page.FindAll("[role=alert]").Should().BeEmpty();

        Time.Advance(BookingRequests.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(2));
    }

    [Fact]
    public async Task Dispose_StopsTheRefreshing()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([]);
        var page = RenderPage();

        await page.Instance.DisposeAsync();
        Time.Advance(BookingRequests.RefreshInterval);

        await Portal.Received(1).ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_WithNoRequests_SaysSo()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns([]);

        RenderPage().Find("[role=status]").TextContent.Should().Contain("Norvia hasn't asked Van Dijk Transport to carry anything yet.");
    }

    [Fact]
    public void Render_WhileLoading_SaysSo()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<IReadOnlyList<CarrierBookingRequestView>>().Task);

        RenderPage().Find("[role=status]").TextContent.Should().Contain("Loading the booking requests");
    }

    [Fact]
    public void Render_WhenTheRequestsCantBeLoaded_SaysSo()
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CarrierBookingRequestView>>(_ => throw new InvalidOperationException("The database is down."));

        RenderPage().Find("[role=alert]").TextContent.Should().Contain("The booking requests couldn't be loaded.");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("brouwer-dranken")]
    public void Render_ForAPersonaThatIsNoCarrier_IsNotFound(string personaId)
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<BookingRequests>(parameters => parameters.Add(page => page.PersonaId, personaId));

        notFound.Should().BeTrue();
    }

    [Fact]
    public void StopText_WithoutAWindow_HasNone()
    {
        var stop = new CarrierBookingStop(null, null, "Rotterdam", null, null, null);

        StopText.Window(stop).Should().BeNull();
        StopText.Address(stop).Should().Be("Rotterdam");
    }

    [Fact]
    public void StopText_WithOneEndOfTheWindow_SaysWhichEnd()
    {
        var from = new DateTime(2030, 10, 2, 7, 30, 0);

        StopText.Window(new CarrierBookingStop(null, null, null, null, from, null)).Should().Be("from Wed 2 Oct 2030, 07:30");
        StopText.Window(new CarrierBookingStop(null, null, null, null, null, from)).Should().Be("until Wed 2 Oct 2030, 07:30");
    }

    [Fact]
    public void GoodsText_ALineWithOnlyAPackageType_NamesIt()
    {
        GoodsText.Of(new CarrierBookingGoodsLine("pallet", null, null, null, null, null)).Should().Be("pallet");
        GoodsText.Of(new CarrierBookingGoodsLine(null, 2, null, null, null, null)).Should().Be("2");
        GoodsText.Of(new CarrierBookingGoodsLine(null, null, null, null, null, null)).Should().BeEmpty();
    }

    private IRenderedComponent<BookingRequests> RenderPage() =>
        Render<BookingRequests>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));
}
