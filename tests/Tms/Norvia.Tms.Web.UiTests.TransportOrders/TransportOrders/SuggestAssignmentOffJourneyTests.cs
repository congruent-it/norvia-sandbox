using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>The Agents are off unless <c>Agents:Enabled</c> is set, which this project's TMS UI leaves unset: the Planner's list has no Suggest button.</summary>
public sealed class SuggestAssignmentOffJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task TheList_WhenTheAgentsAreOff_HasNoSuggestButtonForThePlanner()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await Page.GotoAsync("/transport-orders");

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = number })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Suggest", Exact = false })).ToHaveCountAsync(0);
    }
}
