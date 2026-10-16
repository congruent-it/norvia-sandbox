using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>An operator runs a plan whose Own fleet Shipments go on a Trip that its Driver works to the end, and sees the Trip run in the log. The failed Stops, Exceptions, Re-deliveries and refusals are in the integration tests.</summary>
public sealed partial class TripJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 6;

    // The TMS carries the Trip to the Driver app and the Driver's reports to itself through workers, so the Trip completes a little after the run starts.
    private const float PatienceMilliseconds = 180_000;

    [Fact]
    public async Task AnOperatorRunsAPlanWithTrips_SeesTheTripBuiltDispatchedWorkedByItsDriverAndCompleted_InTheLog()
    {
        // The Drivers' rosters are Monday to Friday, so there is no Shift, and so no Trip for today, at the weekend.
        Assert.SkipWhen(DateTime.UtcNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday, "No Driver has a Shift at the weekend, so no Trip can be for today.");

        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        // Every booking is good and goes to the Own fleet, every order is collected today, and nothing goes wrong on the way.
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Collected today (%)", 100), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 100),
            ("Reassigned (%)", 0), ("Own fleet put on Trips (%)", 100), ("Collections that fail (%)", 0), ("Deliveries that fail (%)", 0),
            ("Damage reported (%)", 0), ("Delivered with reservations (%)", 0),
        })
        {
            await Page.GetByLabel(label).FillAsync(percent.ToString(CultureInfo.InvariantCulture));
        }

        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        var completed = Rows(log, "TripCompleted");
        await Expect(completed).ToHaveCountAsync(1, new() { Timeout = PatienceMilliseconds });

        // The Trip's number is the TMS's, so the test checks its shape, and follows the Trip by it.
        var tripNumber = TripNumber().Match(await completed.InnerTextAsync()).Value;
        tripNumber.Should().NotBeEmpty();
        await Expect(Rows(log, "TripBuilt").Filter(new() { HasText = tripNumber })).ToHaveCountAsync(1);
        await Expect(Rows(log, "TripDispatched").Filter(new() { HasText = tripNumber })).ToHaveCountAsync(1);

        var dispatch = Rows(log, "TripDispatched").Filter(new() { HasText = tripNumber });
        var trail = dispatch.GetByRole(AriaRole.Link, new() { NameRegex = TrailLinks.Name() });
        await Expect(trail).ToHaveAttributeAsync("href", TrailLinks.ConsoleSearch());

        // The Driver works the Trip in order: start, each Stop reported, end.
        var driver = log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator("[data-outcome=DriverReported]") }).Filter(new() { HasText = tripNumber });
        await Expect(driver.Filter(new() { HasText = "Start Trip" })).ToHaveCountAsync(1);
        await Expect(driver.Filter(new() { HasText = "End Trip" })).ToHaveCountAsync(1);
        await Expect(driver.Filter(new() { HasText = "Collected" })).Not.ToHaveCountAsync(0);
        await Expect(driver.Filter(new() { HasText = "Deliver" })).Not.ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-outcome=DriverRefused]")).ToHaveCountAsync(0);

        // The Trip's lines are one filter away, and are still there after a refresh.
        await Page.GetByLabel("Correlation ID, reference, order or Shipment number").FillAsync(tripNumber);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Filter" }).ClickAsync();
        await Expect(Rows(Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }), "TripCompleted")).ToHaveCountAsync(1);
        await Page.ReloadAsync();
        await Expect(Rows(Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }), "TripCompleted").Filter(new() { HasText = tripNumber })).ToHaveCountAsync(1);
    }

    private ILocator Rows(ILocator log, string outcome) =>
        log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator($"[data-outcome={outcome}]") });

    [GeneratedRegex(@"TR-\d{7}")]
    private static partial Regex TripNumber();
}
