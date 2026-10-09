using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Capacity;

/// <summary>
/// The Planner opens the Capacity view (P10) and sees the Own fleet the demo seed names: the Vehicles tab, then the
/// Drivers tab with the licences each Driver holds and the Vehicle types those cover. The Days tab has a row for each open day
/// of the Planning horizon, which the TMS's horizon job opened.
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

    [Fact]
    public async Task ThePlanner_OpensTheDaysTab_AndSeesOneRowForEachOpenDayOfThePlanningHorizon()
    {
        await Page.GotoAsync("/capacity/days");

        var days = Page.GetByRole(AriaRole.Table, new() { Name = "Open days" });
        // The header row, and today with the next 14 days.
        await Expect(days.GetByRole(AriaRole.Row)).ToHaveCountAsync(16);
        await Expect(days.GetByRole(AriaRole.Row).Nth(1)).ToContainTextAsync("6");
    }

    [Fact]
    public async Task ThePlanner_RecordsAnAbsenceAndAnOffTheRoadDay_SeesThemInTheTabs_AndRemovesThem()
    {
        var chosen = DateOnly.FromDateTime(DateTime.Today).AddDays(5);
        var day = chosen.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dayText = chosen.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture);
        var consoleLines = new List<string>();
        Page.PageError += (_, error) => consoleLines.Add("PAGEERROR " + error);
        await Page.GotoAsync("/capacity/drivers");
        // The page is interactive once the lists it reads from the database have loaded.
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading the" })).ToHaveCountAsync(0);

        var absence = Page.GetByRole(AriaRole.Group, new() { Name = "Record an Absence" });
        await absence.GetByRole(AriaRole.Combobox, new() { Name = "Driver" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Sanne Jansen" }).ClickAsync();
        await absence.GetByRole(AriaRole.Textbox, new() { Name = "From" }).FillAsync(day);
        await absence.GetByRole(AriaRole.Textbox, new() { Name = "To" }).FillAsync(day);
        await absence.GetByRole(AriaRole.Textbox, new() { Name = "Remark" }).FillAsync("Dentist");
        await absence.GetByRole(AriaRole.Button, new() { Name = "Record Absence" }).ClickAsync();

        await Expect(absence.GetByRole(AriaRole.Status)).ToContainTextAsync("recorded");
        var absences = Page.GetByRole(AriaRole.Table, new() { Name = "Absences" });
        await Expect(absences.GetByRole(AriaRole.Row).Filter(new() { HasText = "Sanne Jansen" })).ToContainTextAsync("Leave: Dentist");
        await Expect(absences.GetByRole(AriaRole.Row).Filter(new() { HasText = "Sanne Jansen" })).ToContainTextAsync(dayText);
        await absences.GetByRole(AriaRole.Button, new() { Name = "Remove Absence of Sanne Jansen" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "removed" })).ToBeVisibleAsync();
        await Expect(absences.GetByRole(AriaRole.Row).Filter(new() { HasText = "Sanne Jansen" })).ToHaveCountAsync(0);

        await Page.GotoAsync("/capacity");
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading the" })).ToHaveCountAsync(0);
        var offTheRoad = Page.GetByRole(AriaRole.Group, new() { Name = "Take a Vehicle off the road" });
        await offTheRoad.GetByRole(AriaRole.Combobox, new() { Name = "Vehicle" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "TT-301-F" }).ClickAsync();
        await offTheRoad.GetByRole(AriaRole.Textbox, new() { Name = "From" }).FillAsync(day);
        await offTheRoad.GetByRole(AriaRole.Textbox, new() { Name = "To" }).FillAsync(day);
        await offTheRoad.GetByRole(AriaRole.Button, new() { Name = "Take off the road" }).ClickAsync();

        var days = Page.GetByRole(AriaRole.Table, new() { Name = "Off the road days" });
        await Expect(days.GetByRole(AriaRole.Row).Filter(new() { HasText = "TT-301-F" })).ToContainTextAsync("Maintenance");
        await Expect(days.GetByRole(AriaRole.Row).Filter(new() { HasText = "TT-301-F" })).ToContainTextAsync(dayText);
        await days.GetByRole(AriaRole.Button, new() { Name = "Remove Off the road day of TT-301-F" }).ClickAsync();
        await Expect(days.GetByRole(AriaRole.Row).Filter(new() { HasText = "TT-301-F" })).ToHaveCountAsync(0);
    }
}
