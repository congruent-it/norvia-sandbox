using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Trips;

/// <summary>
/// The Planner builds a Trip from two of the demo's Released Own fleet Shipments (P11), drags a collection to the front of the Trip detail's Stops and
/// sees the new order kept, then drags a delivery above its own collection and is told it can't be, with the order unchanged.
/// </summary>
public sealed partial class ReorderStopsJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task ThePlanner_DragsStopsIntoOrder_AndCantPutADeliveryBeforeItsCollection()
    {
        await Page.GotoAsync("/");
        await OpenViewAsync("Shipments to put on Trips");
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading the" })).ToHaveCountAsync(0);

        // The demo's DEMO-TRIP-5 carries 300 kg and DEMO-TRIP-2 carries 2,000 kg.
        var light = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "300 kg" });
        var heavier = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "2,000 kg" });
        await light.GetByRole(AriaRole.Checkbox).ClickAsync();
        await heavier.GetByRole(AriaRole.Checkbox).ClickAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "Put on a Trip" }).GetByRole(AriaRole.Button, new() { Name = "Create Trip" }).ClickAsync();
        var created = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Draft Trip TR-" });
        await Expect(created).ToBeVisibleAsync();
        var trip = TripNumber().Match(await created.TextContentAsync() ?? string.Empty).Value;

        await Page.GotoAsync($"/trips/{trip}");
        var stops = Page.GetByRole(AriaRole.Table, new() { Name = "Stops" });
        var rows = stops.GetByRole(AriaRole.Row);
        await Expect(rows).ToHaveCountAsync(5);

        // Shipments go on the Trip in the order of the list, which is not the order they were ticked in.
        var firstNumber = ShipmentNumber().Match(await rows.Nth(1).TextContentAsync() ?? string.Empty).Value;
        var secondNumber = ShipmentNumber().Match(await rows.Nth(3).TextContentAsync() ?? string.Empty).Value;
        await ExpectOrderAsync(rows, ("Collect", firstNumber), ("Deliver", firstNumber), ("Collect", secondNumber), ("Deliver", secondNumber));

        await rows.Nth(3).DragToAsync(rows.Nth(1));

        await ExpectOrderAsync(rows, ("Collect", secondNumber), ("Collect", firstNumber), ("Deliver", firstNumber), ("Deliver", secondNumber));
        await Page.ReloadAsync();
        await ExpectOrderAsync(rows, ("Collect", secondNumber), ("Collect", firstNumber), ("Deliver", firstNumber), ("Deliver", secondNumber));

        await rows.Nth(4).DragToAsync(rows.Nth(1));

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("can't come before its collection");
        await ExpectOrderAsync(rows, ("Collect", secondNumber), ("Collect", firstNumber), ("Deliver", firstNumber), ("Deliver", secondNumber));
    }

    private static async Task ExpectOrderAsync(ILocator rows, params (string Kind, string Shipment)[] order)
    {
        for (var i = 0; i < order.Length; i++)
        {
            await Expect(rows.Nth(i + 1)).ToContainTextAsync(order[i].Kind);
            await Expect(rows.Nth(i + 1)).ToContainTextAsync(order[i].Shipment);
        }
    }

    [GeneratedRegex("SH-[0-9]{7}")]
    private static partial Regex ShipmentNumber();

    [GeneratedRegex("TR-[0-9]{7}")]
    private static partial Regex TripNumber();
}
