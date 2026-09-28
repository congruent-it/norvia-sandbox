using Microsoft.Playwright;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using Norvia.Tms.Web.UiTests.TransportOrders;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// The Planner assigns a Transport Order to a Carrier, reassigns the Shipment it planned to another Carrier, then
/// releases it from the Planner's list of Shipments to release: it says who carries it now, and it leaves the list
/// once released.
/// </summary>
public sealed class ReassignAndReleaseJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task AShipment_ReassignedThenReleased_ShowsWhoCarriesItNow_AndLeavesTheListOnceReleased()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        var select = row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" });
        await select.ClickAsync();
        var assign = Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Kessler Spedition" }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();
        var outcome = Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status);
        await Expect(outcome).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        var shipmentLink = outcome.GetByRole(AriaRole.Link);
        var shipmentNumber = await shipmentLink.TextContentAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Shipments to release" }).ClickAsync();
        await Expect(Heading("Shipments to release")).ToBeVisibleAsync();
        var shipmentRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(shipmentRow).ToContainTextAsync("Kessler Spedition");

        await shipmentRow.GetByRole(AriaRole.Combobox, new() { Name = $"Reassign {shipmentNumber} to" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Van Dijk Transport" }).ClickAsync();
        await shipmentRow.GetByRole(AriaRole.Button, new() { Name = $"Reassign {shipmentNumber}" }).ClickAsync();
        var reassignOutcome = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = shipmentNumber! });
        await Expect(reassignOutcome).ToContainTextAsync("is now carried by Van Dijk Transport");
        await Expect(shipmentRow).ToContainTextAsync("Van Dijk Transport");

        await shipmentRow.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {shipmentNumber}" }).ClickAsync();
        var release = Page.GetByRole(AriaRole.Group, new() { Name = "Release" });
        await release.GetByRole(AriaRole.Button, new() { Name = "Release selected" }).ClickAsync();

        var releaseOutcome = Page.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Status);
        await Expect(releaseOutcome).ToContainTextAsync($"Shipment {shipmentNumber} is released.");
        await Expect(shipmentRow).ToHaveCountAsync(0);
    }

    private ILocator Heading(string name) => Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 });
}
