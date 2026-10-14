using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>An operator configures a run, previews the plan, runs it and reads the log. The edge cases are in the integration and unit tests.</summary>
public sealed partial class RunJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 8;

    [Fact]
    public async Task AnOperatorConfiguresARun_PreviewsThePlan_RunsIt_AndSeesEachBookingInTheLog()
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        await Page.GetByLabel("Bad bookings (%)").FillAsync("25");
        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"would send {Orders} bookings, 2 of them bad");
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"from seed {seed}");
        var planned = Page.GetByRole(AriaRole.Table, new() { Name = "Planned bookings" });
        var references = await ReferencesAsync(planned);
        references.Should().HaveCount(Orders);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        await Expect(Page.GetByRole(AriaRole.Status).First).ToContainTextAsync($"Completed: {Orders} of {Orders} bookings sent");
        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        foreach (var reference in references.Distinct())
        {
            await Expect(log.GetByRole(AriaRole.Row).Filter(new() { HasText = reference }).First).ToBeVisibleAsync();
        }

        var bookings = log.GetByRole(AriaRole.Row).Filter(new() { HasText = "Send booking" });
        await Expect(bookings).ToHaveCountAsync(Orders);
        var trails = bookings.GetByRole(AriaRole.Link, new() { NameRegex = TrailLinks.Name() });
        await Expect(trails).ToHaveCountAsync(Orders);
        await Expect(trails.First).ToHaveAttributeAsync("href", TrailLinks.ConsoleSearch());

        await Page.ReloadAsync();

        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "Send booking" })).ToHaveCountAsync(Orders);
    }

    private static async Task<string[]> ReferencesAsync(ILocator grid)
    {
        var rows = grid.GetByRole(AriaRole.Row).Filter(new() { HasText = "SIM-" });
        await Expect(rows).ToHaveCountAsync(Orders);
        var text = await rows.AllInnerTextsAsync();
        return [.. text.Select(row => SimReference().Match(row).Value)];
    }

    [GeneratedRegex(@"SIM-\d+-\d+")]
    private static partial Regex SimReference();
}
