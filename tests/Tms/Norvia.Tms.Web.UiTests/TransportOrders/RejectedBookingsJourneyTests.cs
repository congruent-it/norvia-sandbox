using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>
/// Customer service's journey: a Shipper's booking is rejected, and after switching role Customer service finds it
/// with every reason and what the Shipper submitted.
/// </summary>
public sealed class RejectedBookingsJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task ARejectedBooking_IsFoundByCustomerServiceAfterSwitchingRole_WithItsReasonsAndWhatWasSubmitted()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var booking = Bookings.Valid(shipperReference);
        booking["delivery"]!["consignee"]!["name"] = null;
        booking["goodsLines"] = new JsonArray();
        await RejectAsync(booking);

        // A browser that hasn't switched role opens as the Planner.
        await Page.GotoAsync("/");
        await Expect(Heading("Transport Orders waiting for planning")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();

        await Expect(Heading("Rejected bookings")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" })).ToHaveAttributeAsync("aria-pressed", "true");
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await Expect(row).ToContainTextAsync("Brouwer Dranken");

        await row.GetByRole(AriaRole.Link).ClickAsync();

        await Expect(Heading($"Rejected booking {shipperReference}")).ToBeVisibleAsync();
        await Expect(Region("Reasons").GetByRole(AriaRole.Listitem)).ToHaveTextAsync(
            ["The booking has no Consignee name.", "The booking has no goods lines."]);
        await Expect(Region("Collection")).ToContainTextAsync("Waalhaven Oostzijde 88, 3087 BM Rotterdam, NL");
        await Expect(Region("Delivery")).ToContainTextAsync("Oudegracht 112, 3511 AW Utrecht, NL");
        await Expect(Region("Goods")).ToContainTextAsync("No goods lines were given.");
        await Expect(Region("Instructions")).ToContainTextAsync("Ring at the side entrance");

        // The role is remembered: the TMS UI's home now opens Customer service's view, until the Planner switches back.
        await Page.GotoAsync("/");
        await Expect(Heading("Rejected bookings")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();

        await Expect(Heading("Transport Orders waiting for planning")).ToBeVisibleAsync();
    }

    private ILocator Heading(string name) => Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 });

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name });

    private async Task RejectAsync(JsonObject booking)
    {
        using var response = await _tms.SubmitAsync(booking, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
