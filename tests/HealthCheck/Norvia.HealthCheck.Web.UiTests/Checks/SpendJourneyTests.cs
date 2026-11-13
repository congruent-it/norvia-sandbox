using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.HealthCheck.Application.Operators;
using static Microsoft.Playwright.Assertions;

namespace Norvia.HealthCheck.Web.UiTests.Checks;

/// <summary>
/// The spend page's journeys on the modelled spend snapshots. The app's clock is fixed so the two compared weeks are 1 to 7 and 8 to 14
/// November; the expected figures are the weeks' daily averages worked out from the snapshot files, not read back from the code.
/// </summary>
public sealed class SpendJourneyTests(HealthCheckUiFixture fixture) : BrowserPage(fixture)
{
    private ILocator Changes => Page.GetByRole(AriaRole.Region, new() { Name = "Change against the previous period" });

    private ILocator ChangeOf(string name) => Changes.Locator("[data-testid=change]").And(Page.Locator($"[data-resource='{name}']"));

    private ILocator ChangeOfKind(string kind) => Changes.Locator($"[data-testid=change][data-kind={kind}]");

    private ILocator DayRow(string date) => Days.GetByRole(AriaRole.Row).Filter(new() { HasText = date });

    private ILocator Days => Page.GetByRole(AriaRole.Region, new() { Name = "Spend per day" });

    [Fact]
    public async Task AnOperatorOpeningTheSpendPageOnTheChattyTelemetryEstate_SeesTheLogWorkspaceAsTheBiggestRiser_WhileTheVolumeIsFlat()
    {
        await StartWithSpendAsync("chatty-telemetry", OperatorRoles.Cost);

        await Page.GotoAsync("/cost");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Spend", Exact = true })).ToBeVisibleAsync();
        // Average per day, 8 to 14 November against 1 to 7 November: the log workspace 0.55 → 3.31 EUR, the total 4.60 → 7.44 EUR.
        await Expect(ChangeOf("log-workspace")).ToHaveTextAsync("+505%");
        var logWorkspace = Changes.GetByRole(AriaRole.Row).Filter(new() { HasText = "log-workspace" });
        await Expect(logWorkspace).ToContainTextAsync("0.55");
        await Expect(logWorkspace).ToContainTextAsync("3.31");
        await Expect(ChangeOf("app-insights")).ToHaveTextAsync("+21%");
        await Expect(ChangeOfKind("Total")).ToHaveTextAsync("+62%");
        await Expect(ChangeOfKind("Volume")).ToHaveTextAsync("+1%");
        var order = await Changes.Locator("[data-testid=change][data-kind=Resource]").EvaluateAllAsync<string[]>("cells => cells.map(cell => cell.dataset.resource)");
        order.Should().StartWith(["log-workspace", "app-insights"]).And.HaveCount(7);
        order.Last().Should().Be("sql", "a resource with no spend to compare comes last");
    }

    [Fact]
    public async Task AnOperatorOpeningTheSpendPageOnTheFixedEstate_SeesNoResourceRiseBeyondTheVolume()
    {
        await StartWithSpendAsync("fixed", OperatorRoles.Cost);

        await Page.GotoAsync("/cost");

        await Expect(ChangeOf("log-workspace")).ToHaveTextAsync("+1%");
        await Expect(ChangeOf("storage")).ToHaveTextAsync("+1%");
        await Expect(ChangeOf("sql")).ToHaveTextAsync("n/a");
        await Expect(ChangeOfKind("Total")).ToHaveTextAsync("+1%");
        await Expect(ChangeOfKind("Volume")).ToHaveTextAsync("+1%");
    }

    [Fact]
    public async Task AnOperatorOpeningTheSpendPage_SeesTheSpendOfEachWholeDayNextToThatDaysTransportOrders_AndNoProvisionalDay()
    {
        await StartWithSpendAsync("fixed", OperatorRoles.Cost);

        await Page.GotoAsync("/cost");

        // 1 November: the log workspace 0.53 EUR, 4.50 EUR in all (0.53 + 0.29 + 2.35 + 0.34 + 0.00 + 0.11 + 0.88), for 38 Transport Orders.
        var firstDay = DayRow("2026-11-01");
        await Expect(firstDay).ToContainTextAsync("0.53");
        await Expect(firstDay).ToContainTextAsync("4.50");
        await Expect(firstDay).ToContainTextAsync("38");
        await Expect(DayRow("2026-11-14")).ToHaveCountAsync(1);
        await Expect(Days.GetByRole(AriaRole.Columnheader, new() { Name = "Transport Orders" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("(provisional)")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Note)).ToContainTextAsync("only whole past days are shown");
    }

    [Theory]
    [InlineData(OperatorRoles.Run)]
    [InlineData(OperatorRoles.Read)]
    public async Task AnOperatorWithoutTheCostRole_IsRefused_AndSeesNoLinkToTheSpendPage(string role)
    {
        await StartWithSpendAsync("chatty-telemetry", role);

        var response = await Page.GotoAsync("/cost");

        response!.Status.Should().Be((int)HttpStatusCode.Forbidden);
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Spend" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnOperatorWithTheCostRole_FindsTheSpendPageFromTheAppBar()
    {
        await StartWithSpendAsync("fixed", OperatorRoles.Cost);
        await Page.GotoAsync("/");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Spend" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/cost$"));
        await Expect(Days).ToBeVisibleAsync();
    }
}
