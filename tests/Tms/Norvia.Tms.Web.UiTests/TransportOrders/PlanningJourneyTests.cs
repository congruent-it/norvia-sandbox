using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>
/// The Planner plans a Transport Order: it appears in the list waiting for planning, the Planner assigns it to one of the
/// demo seed's active Carriers, which the TMS UI sends through the TMS API, and it leaves the list as Planned, with a
/// Shipment that says who carries it. The demo seed's inactive Carrier, Havenstad Koeriers, is never offered.
/// </summary>
public sealed class PlanningJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task ATransportOrderAssignedToACarrier_LeavesTheList_AndItsShipmentSaysWhoCarriesIt()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await Expect(Heading("Transport Orders waiting for planning")).ToBeVisibleAsync();
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await Expect(row).ToContainTextAsync("Rotterdam, NL");
        await Expect(row).ToContainTextAsync("Utrecht, NL");

        // Clicked rather than checked: Fluent UI's checkbox shows its new state a moment after the click, which
        // CheckAsync doesn't wait for.
        var select = row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" });
        await select.ClickAsync();
        await Expect(select).ToBeCheckedAsync();
        var assign = Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await Expect(assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected (1)" })).ToBeVisibleAsync();
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Option, new() { Name = "Havenstad Koeriers" })).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Option, new() { Name = "Kessler Spedition" }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();

        var outcome = Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status);
        await Expect(outcome).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        await Expect(outcome).ToContainTextAsync("carried by Kessler Spedition");
        await Expect(row).ToHaveCountAsync(0);

        await outcome.GetByRole(AriaRole.Link).ClickAsync();

        await Expect(Heading(new Regex("^Shipment SH-"))).ToBeVisibleAsync();
        var shipment = Page.GetByRole(AriaRole.Region, new() { Name = "Shipment" });
        await Expect(shipment).ToContainTextAsync("Planned");
        await Expect(shipment).ToContainTextAsync("Kessler Spedition");

        await shipment.GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Heading($"Transport Order {number}")).ToBeVisibleAsync();
        var transportOrder = Page.GetByRole(AriaRole.Region, new() { Name = "Transport Order" });
        await Expect(transportOrder).ToContainTextAsync("Planned");
        await Expect(transportOrder.GetByRole(AriaRole.Link, new() { NameRegex = new("^SH-") })).ToBeVisibleAsync();
    }

    private ILocator Heading(string name) => Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 });

    private ILocator Heading(Regex name) => Page.GetByRole(AriaRole.Heading, new() { NameRegex = name, Level = 1 });
}
