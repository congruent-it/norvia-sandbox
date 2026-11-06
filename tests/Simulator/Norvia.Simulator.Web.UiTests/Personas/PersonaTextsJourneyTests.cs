using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Personas;

/// <summary>
/// An operator configures a run whose Carriers decline, reads the pools of Carrier texts in the plan preview (their source and a sample), starts the run and reads the log,
/// which names the pool and the index of each text and never the text. The Agents are on, with the scripted fake model. The draws, the validation and the other fallbacks are in the unit and integration tests.
/// </summary>
public sealed partial class PersonaTextsJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 4;

    private readonly SimulatorUiFixture _simulator = simulator;

    [Fact]
    public async Task AnOperatorPreviewsAPlanWithThePersonaTexts_SeesEachPoolsSourceAndASample_AndTheRunLogNamesThePoolAndIndexAndNeverTheText()
    {
        await ConfigureDecliningRunAsync(_simulator.WithAgents);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        var pools = Page.GetByTestId("pool");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Carrier texts", Level = 2 })).ToBeVisibleAsync();
        await Expect(pools.First).ToBeVisibleAsync();
        var texts = await PoolTextsAsync(pools);
        texts.Should().NotBeEmpty();
        foreach (var pool in await pools.AllAsync())
        {
            await Expect(pool).ToContainTextAsync(AgentSource());
            await Expect(pool).ToContainTextAsync("A sample:");
        }

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "built-in pool is used" })).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await AssertLogNamesPoolsButNoTextAsync(texts);
    }

    [Fact]
    public async Task WhenTheModelFailsForAProfile_ThePreviewSaysSoAndUsesTheBuiltInPool_AndTheRunStillStarts()
    {
        await ConfigureDecliningRunAsync(_simulator.WithFailingAgent);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        var note = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "built-in pool is used" }).First;
        await Expect(note).ToContainTextAsync("The model couldn't write the terse-dutch");
        var terse = Page.GetByTestId("pool").Filter(new() { HasText = "terse-dutch" });
        await Expect(terse.First).ToContainTextAsync("Source: built-in.");
        var others = Page.GetByTestId("pool").Filter(new() { HasNotText = "terse-dutch" });
        await Expect(others.First).ToContainTextAsync(AgentSource());
        var texts = await PoolTextsAsync(Page.GetByTestId("pool"));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await AssertLogNamesPoolsButNoTextAsync(texts);
    }

    // A run in which every booking is good and planned, and every Carrier declines with the reason Other, so each decline carries a text from a pool.
    private async Task ConfigureDecliningRunAsync(Uri simulator)
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync(simulator.ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

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
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
    }

    // The sampled texts of the pools in the preview: what the log must never show.
    private static async Task<List<string>> PoolTextsAsync(ILocator pools) =>
        [.. (await pools.Locator("li").AllInnerTextsAsync()).Select(text => text.Trim()).Where(text => text.Length > 0)];

    private async Task AssertLogNamesPoolsButNoTextAsync(IReadOnlyList<string> texts)
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        var declined = log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator("[data-outcome=Declined]") });
        await Expect(declined).ToHaveCountAsync(Orders, new() { Timeout = 90_000 });
        foreach (var row in await declined.AllAsync())
        {
            await Expect(row).ToContainTextAsync(PoolReference());
        }

        var logText = await log.InnerTextAsync();
        foreach (var text in texts)
        {
            logText.Should().NotContain(text, "the log names a pool and an index, never a text");
        }
    }

    private static Regex AgentSource() => AgentSourceRegex();

    private static Regex PoolReference() => PoolReferenceRegex();

    [GeneratedRegex(@"Source: agent, run \d+\.")]
    private static partial Regex AgentSourceRegex();

    [GeneratedRegex(@"pool \S+, index \d+")]
    private static partial Regex PoolReferenceRegex();
}
