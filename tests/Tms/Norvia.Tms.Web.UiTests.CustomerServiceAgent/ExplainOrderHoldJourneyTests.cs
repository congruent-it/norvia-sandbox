using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// Customer service asks the assistant to explain a row of the two lists that hold an order back, against the scripted fake model: a rating exceptions row (the demo seed's Rate Card has
/// no Lane into Germany, so a booking to Düsseldorf fails with <c>NoMatchingLane</c>) and a not-yet-billable row (a delivered order without a Proof of delivery). The answer points to
/// Re-rate or Release; it never runs them.
/// </summary>
public sealed class ExplainOrderHoldJourneyTests(CustomerServiceAgentUiFixture agent) : BrowserPage(agent)
{
    // The assistant reads the order through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    private const float AgentTimeout = 30_000;

    [Fact]
    public async Task Explain_OnARatingExceptionsRow_SaysWhyRatingFailedAndPointsToReRateWithoutRunningIt()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference).DeliveredToDusseldorf());
        await OpenListAsync("/rating-exceptions", "Rating exceptions");
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await Expect(row.GetByRole(AriaRole.Gridcell, new() { Name = "No matching Lane", Exact = true })).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Button, new() { Name = $"Explain {number}" }).ClickAsync();

        var explanation = row.GetByTestId("explanation");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Why Rating failed" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync("no Lane for this origin and destination");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Evidence" })).ToBeVisibleAsync();
        await Expect(explanation.GetByRole(AriaRole.Listitem)).ToContainTextAsync(["Rating failed with NoMatchingLane"]);
        await Expect(explanation.Locator("cite")).ToContainTextAsync("get_transport_order");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "What fixes it" })).ToBeVisibleAsync();
        await Expect(explanation).ToContainTextAsync("ask whoever owns the Rate Card to fix it");
        await Expect(explanation).ToContainTextAsync("Re-rate action on the rating exceptions list");
        await Expect(explanation).ToContainTextAsync("you decide");

        // It points to Re-rate and leaves it to Customer service: nothing was re-rated, so the order is still listed and no outcome was announced.
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Re-rate {number}" })).ToBeEnabledAsync();
        await Expect(row.GetByRole(AriaRole.Gridcell, new() { Name = "No matching Lane", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Rating failed again" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "is now Rated" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Explain_OnANotYetBillableRow_SaysWhatHoldsTheOrderBackAndPointsToReleaseWithoutRunningIt()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-10));
        await OpenListAsync("/not-yet-billable", "Not yet billable");
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(row).ToContainTextAsync("No Proof of delivery");
        var number = (await row.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^TO-") }).TextContentAsync())!.Trim();

        await row.GetByRole(AriaRole.Button, new() { Name = $"Explain {number}" }).ClickAsync();

        var explanation = row.GetByTestId("explanation");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "What holds it back" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync($"Transport Order {number} is not yet billable because it has no Proof of delivery");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Evidence" })).ToBeVisibleAsync();
        await Expect(explanation.GetByRole(AriaRole.Listitem)).ToContainTextAsync(["Billing is blocked by NoProofOfDelivery."]);
        await Expect(explanation.Locator("cite")).ToContainTextAsync("get_transport_order");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "What you can do" })).ToBeVisibleAsync();
        await Expect(explanation).ToContainTextAsync("ask the Carrier for the Proof of delivery, or release the order without one");
        await Expect(explanation).ToContainTextAsync("you decide");

        // It points to Release and leaves it to Customer service: the release form is not open and the order is still held back.
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "without Proof of delivery", Exact = false })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "released without a Proof of delivery" })).ToHaveCountAsync(0);
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "without Proof of delivery", Exact = false })).ToBeEnabledAsync();
        await Expect(row).ToContainTextAsync("No Proof of delivery");
    }

    // The list reads the role from the request while it prerenders, so Customer service opens it with a full page load.
    private async Task OpenListAsync(string address, string title)
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();
        await Page.GotoAsync(address);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = title, Level = 1 })).ToBeVisibleAsync();
    }
}
