using Microsoft.Playwright;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>
/// The Planner sees the commercial side of a Transport Order: accepted while the list is open, it becomes Rated there,
/// and its detail shows the Freight Charge. The Rate Card is the demo seed's Brouwer Dranken one, version 1, which
/// prices <see cref="Bookings.Valid"/> (910.5 kg, Rotterdam to Utrecht) at 10.20 per 100 kg.
/// </summary>
public sealed class RatingJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task ATransportOrderAcceptedWhileTheListIsOpen_BecomesRatedWithoutARefresh_AndOpensWithItsFreightCharge()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        await Page.GotoAsync("/transport-orders");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders waiting for planning" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Loading the Transport Orders waiting for planning")).ToBeHiddenAsync();

        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await Expect(row.GetByRole(AriaRole.Gridcell, new() { Name = "Rated", Exact = true })).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        var rating = Page.GetByRole(AriaRole.Region, new() { Name = "Rating" });
        await Expect(rating).ToContainTextAsync("Rated");
        await Expect(rating).ToContainTextAsync("RC-0001, version 1");
        await Expect(rating).ToContainTextAsync("EUR 92.87");
        var chargeLines = rating.GetByRole(AriaRole.Table, new() { Name = "Charge lines" }).GetByRole(AriaRole.Row);
        await Expect(chargeLines.Filter(new() { HasText = "Freight" })).ToContainTextAsync("EUR 92.87");
    }
}
