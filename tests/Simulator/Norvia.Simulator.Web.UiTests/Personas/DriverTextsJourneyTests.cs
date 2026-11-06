using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Simulator.Web.UiTests.Runs;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Personas;

/// <summary>
/// An operator configures a run whose Own fleet Driver fails collections and deliveries and reports damage, reads the pools of Driver texts in the plan preview (their source,
/// the Driver that writes in them and a sample), starts the run and reads the log, which names the pool and the index of each Driver's text and never the text (#486).
/// The Agents are on, with the scripted fake model. The draws, the validation, the fallback and the replay are in the unit and integration tests.
/// </summary>
public sealed partial class DriverTextsJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 10;
    private const string Driver = "DRV-0002";

    // The Trip completes a little after the run starts (see TripJourneyTests), and the Drivers' reports reach the TMS through a worker after that.
    private const float PatienceMilliseconds = 180_000;

    private readonly SimulatorUiFixture _simulator = simulator;

    [Fact(SkipUnless = nameof(CollectionHours.TodayIsStillOpen), SkipType = typeof(CollectionHours), Skip = CollectionHours.Reason)]
    public async Task AnOperatorPreviewsAPlanWithDriverTexts_SeesEachPoolsSourceAndDriver_AndTheRunsDriverReportsDrawFromThemAndTheLogNeverShowsTheText()
    {
        await ConfigureRunAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Driver texts", Level = 2 })).ToBeVisibleAsync();
        var pools = Page.GetByTestId("driver-pool");
        await Expect(pools).ToHaveCountAsync(3);
        foreach (var kind in new[] { "Collection failed remark", "Delivery failed remark", "Damage remark" })
        {
            var pool = pools.Filter(new() { HasText = kind });
            await Expect(pool).ToHaveCountAsync(1);
            await Expect(pool).ToContainTextAsync("terse-dutch");
            await Expect(pool).ToContainTextAsync(AgentSource());
            await Expect(pool).ToContainTextAsync(Driver);
            await Expect(pool).ToContainTextAsync("A sample:");
        }

        var texts = new List<string>(
            (await pools.Locator("li").AllInnerTextsAsync()).Select(text => text.Trim()).Where(text => text.Length > 0));
        texts.Should().NotBeEmpty();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        await Expect(Rows(log, "TripCompleted")).ToHaveCountAsync(1, new() { Timeout = PatienceMilliseconds });

        // Each kind of Driver report that carries a text names the Driver's pool and the index drawn from it.
        foreach (var (action, category) in new[]
        {
            ("Collection failed", "DriverCollectionFailedRemark"), ("Delivery failed", "DriverDeliveryFailedRemark"), ("Report damage", "DriverDamageRemark"),
        })
        {
            var reports = Rows(log, "DriverReported").Filter(new() { HasText = action });
            await Expect(reports.First).ToBeVisibleAsync();
            foreach (var report in await reports.AllAsync())
            {
                await Expect(report).ToContainTextAsync(PoolReference($"terse-dutch/{category}/nl"));
            }
        }

        var logText = await log.InnerTextAsync();
        foreach (var text in texts)
        {
            logText.Should().NotContain(text, "the log names a pool and an index, never a text");
        }
    }

    // A run in which every booking is good, collected today and goes to the Own fleet's one Driver, who fails some collections and deliveries and reports some damage.
    private async Task ConfigureRunAsync()
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync(_simulator.WithAgentsAndDriverFleet.ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Collected today (%)", 100), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 100),
            ("Reassigned (%)", 0), ("Own fleet put on Trips (%)", 100), ("Collections that fail (%)", 20), ("Deliveries that fail (%)", 40),
            ("Damage reported (%)", 30), ("Delivered with reservations (%)", 0),
        })
        {
            await Page.GetByLabel(label).FillAsync(percent.ToString(CultureInfo.InvariantCulture));
        }

        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
    }

    private ILocator Rows(ILocator log, string outcome) =>
        log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator($"[data-outcome={outcome}]") });

    private static Regex AgentSource() => AgentSourceRegex();

    private static Regex PoolReference(string pool) => new($@"pool {Regex.Escape(pool)}, index \d+", RegexOptions.None, TimeSpan.FromSeconds(5));

    [GeneratedRegex(@"Source: agent, run \d+\.")]
    private static partial Regex AgentSourceRegex();
}
