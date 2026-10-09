using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Capacity;

/// <summary>
/// The Planner opens the Capacity view (P10) and sees the Own fleet the demo seed names: the Vehicles tab, then the
/// Drivers tab with the licences each Driver holds and the Vehicle types those cover.
/// </summary>
public sealed class FleetJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task ThePlanner_OpensCapacity_AndReadsTheVehiclesAndThenTheDrivers()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Capacity" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Capacity", Level = 1 })).ToBeVisibleAsync();
        var vehicles = Page.GetByRole(AriaRole.Table, new() { Name = "Vehicles" });
        await Expect(vehicles.GetByRole(AriaRole.Row).Filter(new() { HasText = "VN-101-A" })).ToContainTextAsync("Van");
        await Expect(vehicles.GetByRole(AriaRole.Row).Filter(new() { HasText = "TT-301-F" })).ToContainTextAsync("24,000 kg");

        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Capacity" }).GetByRole(AriaRole.Link, new() { Name = "Drivers" }).ClickAsync();

        var drivers = Page.GetByRole(AriaRole.Table, new() { Name = "Drivers" });
        await Expect(drivers.GetByRole(AriaRole.Row).Filter(new() { HasText = "Anouk de Vries" })).ToContainTextAsync("B, C, CE");
        await Expect(drivers.GetByRole(AriaRole.Row).Filter(new() { HasText = "Sanne Jansen" })).ToContainTextAsync("Van");
    }
}
