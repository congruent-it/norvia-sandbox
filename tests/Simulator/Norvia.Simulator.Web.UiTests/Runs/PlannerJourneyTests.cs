using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>An operator runs a plan with assignment and opens a log line's trail. The refusals, Own fleet and reassignments are in the integration tests.</summary>
public sealed partial class PlannerJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 6;

    [Fact]
    public async Task AnOperatorRunsAPlanWithAssignment_SeesTheOrdersPlanned_AndOpensTheTrailOfAnAssignment()
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        // Every booking is good and every accepted order is meant for a Carrier, so each of them is planned.
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        await Page.GetByLabel("Bad bookings (%)").FillAsync("0");
        await Page.GetByLabel("Assigned by the Planner (%)").FillAsync("100");
        await Page.GetByLabel("To Own fleet where it fits (%)").FillAsync("0");
        await Page.GetByLabel("Reassigned (%)").FillAsync("0");
        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Expect(Page.GetByTestId("planner-funnel")).ToContainTextAsync($"assigns {Orders} of the {Orders} orders");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        await Expect(Page.GetByRole(AriaRole.Status).First).ToContainTextAsync($"{Orders} of {Orders} Transport Orders planned");

        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        var assignments = log.GetByRole(AriaRole.Row).Filter(new() { HasText = "Assign Transport Order" });
        await Expect(assignments).ToHaveCountAsync(Orders);
        await Expect(assignments.Filter(new() { Has = Page.Locator("[data-outcome=Planned]") })).ToHaveCountAsync(Orders);

        var assignment = assignments.First;
        await Expect(assignment).ToContainTextAsync(TransportOrderNumber());
        await Expect(assignment).ToContainTextAsync(ShipmentNumber());

        var trail = assignment.GetByRole(AriaRole.Link, new() { NameRegex = TrailLink() });
        await Expect(trail).ToHaveAttributeAsync("href", ConsoleSearch());
        var correlationId = ConsoleSearch().Match((await trail.GetAttributeAsync("href"))!).Groups["key"].Value;

        var console = await Page.Context.RunAndWaitForPageAsync(() => trail.ClickAsync());
        await Expect(console).ToHaveURLAsync(new Regex($"key={Regex.Escape(correlationId)}"));
    }

    // The Transport Order and Shipment numbers are the TMS's, so the test checks their shape, not their values.
    [GeneratedRegex(@"TO-\d{7}")]
    private static partial Regex TransportOrderNumber();

    [GeneratedRegex(@"SH-\d{7}")]
    private static partial Regex ShipmentNumber();

    [GeneratedRegex(@"/\?key=(?<key>[0-9a-fA-F-]{32,})&within=all$")]
    private static partial Regex ConsoleSearch();

    [GeneratedRegex("Open the Message trail of .* in the Integration console")]
    private static partial Regex TrailLink();
}
