using Microsoft.Playwright;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>
/// Customer service works the rating exceptions. The demo seed's Brouwer Dranken Rate Card deliberately has no Lane into
/// Germany, so a booking from Rotterdam to Düsseldorf fails with <c>NoMatchingLane</c>, which Customer service reads as
/// "No matching Lane". Its Rate Card isn't corrected, so a re-rate, which the TMS UI sends through the TMS API, fails
/// again and the order stays in the list.
/// </summary>
public sealed class RatingExceptionsJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task AnOrderNoLaneCovers_IsListedForCustomerServiceAsNoMatchingLane_AndStaysThereWhenAReRateFailsAgain()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference).DeliveredToDusseldorf());

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Rating exceptions" }).ClickAsync();

        await Expect(Heading("Rating exceptions")).ToBeVisibleAsync();
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await Expect(row).ToContainTextAsync("Brouwer Dranken");
        await Expect(row.GetByRole(AriaRole.Gridcell, new() { Name = "No matching Lane", Exact = true })).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Button, new() { Name = $"Re-rate {number}" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = $"Rating failed again for Transport Order {number}" })).ToBeVisibleAsync();
        await Expect(row.GetByRole(AriaRole.Gridcell, new() { Name = "No matching Lane", Exact = true })).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Heading($"Transport Order {number}")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Rating" })).ToContainTextAsync("Rating failed");
    }

    private ILocator Heading(string name) => Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 });
}
