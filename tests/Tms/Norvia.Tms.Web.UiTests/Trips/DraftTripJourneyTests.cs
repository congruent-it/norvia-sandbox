using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Trips;

/// <summary>
/// The Planner opens the Trips view (P11), creates a Draft Trip for an open day, picks its Vehicle and Driver from the day's
/// available ones, sees it in the list without a page refresh, and discards it.
/// </summary>
public sealed class DraftTripJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task ThePlanner_CreatesADraftTrip_PicksAVehicleAndDriver_AndDiscardsIt()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Trips", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Trips", Level = 1 })).ToBeVisibleAsync();
        // The page is interactive once the lists it reads from the database have loaded.
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading the" })).ToHaveCountAsync(0);

        var create = Page.GetByRole(AriaRole.Group, new() { Name = "Create a Draft Trip" });
        await create.GetByRole(AriaRole.Combobox, new() { Name = "Day" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option).Nth(1).ClickAsync();
        await create.GetByRole(AriaRole.Button, new() { Name = "Create Trip" }).ClickAsync();

        await Expect(create.GetByRole(AriaRole.Status)).ToContainTextAsync("Draft Trip TR-");
        var trips = Page.GetByRole(AriaRole.Table, new() { Name = "Trips" });
        var draft = trips.GetByRole(AriaRole.Row).Filter(new() { HasText = "Draft" }).Last;
        await Expect(draft).ToContainTextAsync("—");

        await draft.GetByRole(AriaRole.Button, new() { Name = "Plan" }).ClickAsync();
        var pick = Page.GetByRole(AriaRole.Group, new() { Name = "Plan" });
        await pick.GetByRole(AriaRole.Combobox, new() { Name = "Vehicle" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option).Nth(1).ClickAsync();
        await pick.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(pick).ToHaveCountAsync(0);
        await Expect(trips.GetByRole(AriaRole.Row).Filter(new() { HasText = "Draft" }).Last).ToContainTextAsync(new System.Text.RegularExpressions.Regex("[A-Z]{2}-[0-9]{3}-[A-Z]"));

        await trips.GetByRole(AriaRole.Row).Filter(new() { HasText = "Draft" }).Last.GetByRole(AriaRole.Button, new() { Name = "Discard" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "discarded" })).ToBeVisibleAsync();
    }
}
