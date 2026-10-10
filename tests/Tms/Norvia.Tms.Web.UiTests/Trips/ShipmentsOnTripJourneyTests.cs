using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Trips;

/// <summary>
/// The Planner opens the Shipments to put on Trips (P11), selects the demo's Shipment that is too heavy for a van, creates a Trip for its collection
/// day, sees its Stops in the Trip detail, and is told it can't go on the van (PayloadExceeded). The demo seed gives the Released Own fleet Shipments.
/// </summary>
public sealed class ShipmentsOnTripJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task ThePlanner_CreatesATripFromTheHeavyShipment_SeesItsStops_AndCantPutItOnTheVan()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Shipments to put on Trips" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Shipments to put on Trips", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading the" })).ToHaveCountAsync(0);

        var heavy = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "1,500 kg" });
        await heavy.GetByRole(AriaRole.Checkbox).ClickAsync();
        var put = Page.GetByRole(AriaRole.Group, new() { Name = "Put on a Trip" });
        await put.GetByRole(AriaRole.Button, new() { Name = "Create Trip" }).ClickAsync();

        var created = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Draft Trip TR-" });
        await Expect(created).ToBeVisibleAsync();
        var number = Regex.Match(await created.TextContentAsync() ?? string.Empty, "TR-[0-9]{7}").Value;
        await Expect(heavy).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Trips", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Trip {number}", Level = 1 })).ToBeVisibleAsync();
        var stops = Page.GetByRole(AriaRole.Table, new() { Name = "Stops" });
        await Expect(stops.GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
        await Expect(stops).ToContainTextAsync("Collect");
        await Expect(stops).ToContainTextAsync("Deliver");

        await Page.GetByRole(AriaRole.Link, new() { Name = "All Trips" }).ClickAsync();
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = number }).GetByRole(AriaRole.Button, new() { Name = "Plan" }).ClickAsync();
        var plan = Page.GetByRole(AriaRole.Group, new() { Name = "Plan" });
        await plan.GetByRole(AriaRole.Combobox, new() { Name = "Vehicle" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "VN-101-A" }).ClickAsync();
        await plan.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(plan.GetByRole(AriaRole.Alert)).ToContainTextAsync("more than Vehicle VEH-0001 can carry");
    }
}
