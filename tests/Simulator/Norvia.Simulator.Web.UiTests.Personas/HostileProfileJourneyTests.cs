using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Personas;

/// <summary>
/// The opt-in hostile profile (#487) in the browser, with the Agents on (the scripted fake model): it is selectable only on purpose, per Carrier, on the Configure screen;
/// no Preset turns it on; Preview plan labels it; and the run page and the log of a hostile run carry the label (<c>profile: hostile</c>).
/// </summary>
public sealed class HostileProfileJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    // The Planner chooses the Carrier of each Shipment, and the seed is random: of four orders the chosen Carrier gets none about a quarter of the time. With sixteen it
    // does so about once in several hundred runs.
    private const int Orders = 16;
    private const string Carrier = "CAR-0001";

    private readonly SimulatorUiFixture _simulator = simulator;

    [Fact]
    public async Task TheHostileProfile_IsOffByDefault_AndNoPresetTurnsItOn_SoAPlanIsNotLabelled()
    {
        await OpenConfigureAsync();
        await OpenHostileChoicesAsync();

        var choices = Page.GetByTestId("hostile-choices");
        var boxes = choices.GetByRole(AriaRole.Checkbox);
        await Expect(boxes.First).ToBeVisibleAsync();
        foreach (var box in await boxes.AllAsync())
        {
            await Expect(box).Not.ToBeCheckedAsync();
        }

        // Every Preset, picked in turn, leaves the choices as they were.
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Preset" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Option).First).ToBeVisibleAsync();
        var presets = await Page.GetByRole(AriaRole.Option).AllInnerTextsAsync();
        await Page.Keyboard.PressAsync("Escape");
        presets.Should().NotBeEmpty();
        foreach (var preset in presets.Select(name => name.Trim()).Where(name => name.Length > 0 && name != "Custom"))
        {
            await Page.GetByRole(AriaRole.Combobox, new() { Name = "Preset" }).ClickAsync();
            await Page.GetByRole(AriaRole.Option, new() { Name = preset, Exact = true }).ClickAsync();
            foreach (var box in await boxes.AllAsync())
            {
                await Expect(box).Not.ToBeCheckedAsync();
            }
        }

        await FillRunAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Expect(Page.GetByTestId("hostile-banner")).ToHaveCountAsync(0);
        await Expect(Page.GetByText("HOSTILE")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnOperatorTicksACarrierOnPurpose_AndPreviewPlanLabelsTheRunAndThePoolsAsHostile()
    {
        await OpenConfigureAsync();
        await FillRunAsync();
        await TickHostileCarrierAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        var banner = Page.GetByTestId("hostile-banner");
        await Expect(banner).ToBeVisibleAsync();
        await Expect(banner).ToContainTextAsync("profile: hostile");
        await Expect(banner).ToContainTextAsync(Carrier);
        var hostilePools = Page.GetByTestId("pool").Filter(new() { HasText = "HOSTILE" });
        await Expect(hostilePools.First).ToBeVisibleAsync();
        await Expect(hostilePools.First).ToContainTextAsync("hostile");
    }

    [Fact]
    public async Task AHostileRun_IsLabelledOnTheRunPageAndOnTheFirstLineAndTheDeclinesOfItsLog()
    {
        await OpenConfigureAsync();
        await FillRunAsync();
        await TickHostileCarrierAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("HOSTILE");
        var banner = Page.GetByTestId("hostile-banner");
        await Expect(banner).ToContainTextAsync("profile: hostile");
        await Expect(banner).ToContainTextAsync(Carrier);

        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        await Expect(log).ToContainTextAsync("HOSTILE RUN (profile: hostile)");
        var declined = log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator("[data-outcome=Declined]") });
        await Expect(declined.First).ToBeVisibleAsync(new() { Timeout = 90_000 });

        // The Carrier chosen declines in the hostile profile: its lines name the hostile pool and an index, and the others keep their own.
        var hostile = declined.Filter(new() { HasText = "profile: hostile" });
        await Expect(hostile.First).ToContainTextAsync("profile: hostile, pool ");
    }

    private async Task OpenConfigureAsync()
    {
        await Page.GotoAsync(_simulator.WithAgents.ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");
    }

    // The group is collapsed until a Carrier or Driver is chosen.
    private async Task OpenHostileChoicesAsync()
    {
        var choices = Page.GetByTestId("hostile-choices");
        if (!await choices.IsVisibleAsync())
        {
            // The sticky Preview bar can cover the heading once it is scrolled into view, so the click is sent to it directly.
            await Page.GetByText("Hostile profile (off by default)").DispatchEventAsync("click");
        }

        await Expect(choices).ToBeVisibleAsync();
    }

    private async Task TickHostileCarrierAsync()
    {
        await OpenHostileChoicesAsync();
        var box = Page.GetByTestId("hostile-choices").GetByRole(AriaRole.Checkbox, new() { Name = $"({Carrier})" });
        await box.ClickAsync();
        await Expect(box).ToBeCheckedAsync();
    }

    // A run in which every booking is good and planned, and every Carrier declines with the reason Other, so each decline carries a text from a pool.
    private async Task FillRunAsync()
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");

        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 0), ("Reassigned (%)", 0),
            ("Released by the Planner (%)", 100), ("Confirmed by the Carrier (%)", 0), ("Declined by the Carrier (%)", 100),
        })
        {
            await Page.GetByLabel(label).FillAsync(percent.ToString(CultureInfo.InvariantCulture));
        }

        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
    }
}
