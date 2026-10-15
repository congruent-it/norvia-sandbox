using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>An operator runs a plan whose Carriers answer, track and deliver every Shipment, and sees them Delivered in the log and in the Shipper's tracking view. The failure mixes and shares are in the integration tests.</summary>
public sealed partial class CarrierJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 4;
    private const string ShipperPersona = "brouwer-dranken";

    // The TMS processes what the Carriers send after it accepts it, so Delivered shows a little after the run.
    private const float PatienceMilliseconds = 90_000;

    // Collected, departed, arrived, out for delivery and delivered.
    private const int TrackingEventsPerShipment = 5;

    private readonly SimulatorUiFixture _simulator = simulator;

    [Fact]
    public async Task AnOperatorRunsAPlanWhoseCarriersDeliverEverything_SeesTheShipmentsDeliveredInTheLog_AndInTheShippersTrackingView()
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        // Every booking is good, planned and released, and every Carrier confirms, tracks, delivers and proves the delivery.
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 0), ("Reassigned (%)", 0),
            ("Released by the Planner (%)", 100), ("Confirmed by the Carrier (%)", 100), ("Declined by the Carrier (%)", 0), ("Tracked (%)", 100),
            ("Exceptions on the way (%)", 0), ("Delivered (%)", 100), ("With a Proof of delivery (%)", 100), ("Failed collections cancelled (%)", 0),
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
        var delivered = Rows(log, "ProofOfDeliverySent");
        await Expect(delivered).ToHaveCountAsync(Orders, new() { Timeout = PatienceMilliseconds });
        await Expect(Rows(log, "TrackingSent")).ToHaveCountAsync(Orders * TrackingEventsPerShipment);
        var references = (await delivered.AllInnerTextsAsync()).Select(row => SimReference().Match(row).Value).Distinct().ToList();
        references.Should().HaveCount(Orders);

        var portal = await Page.Context.NewPageAsync();
        await portal.GotoAsync(new Uri(_simulator.PartnerPortal, $"/personas/{ShipperPersona}/tracking").ToString());
        await Expect(portal.GetByRole(AriaRole.Heading, new() { Name = "Tracking", Level = 1 })).ToBeVisibleAsync();
        foreach (var reference in references)
        {
            var row = portal.GetByRole(AriaRole.Row).Filter(new() { HasText = reference });
            await Expect(row).ToContainTextAsync("Delivered", new() { Timeout = PatienceMilliseconds });
        }
    }

    private ILocator Rows(ILocator log, string outcome) =>
        log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator($"[data-outcome={outcome}]") });

    [GeneratedRegex(@"SIM-\d+-\d+")]
    private static partial Regex SimReference();
}
