using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgentPlan;

/// <summary>
/// The Planner plans the waiting list in one go, against the scripted fake model: a Suggestion per order, each accepted or ignored on its own, and a run over its cap (two orders here) that says
/// where it stopped. Accepting a Suggestion pre-selects that order only and executes nothing. Which Agent runs and tool calls a plan makes is read in the integration tests, not here.
/// </summary>
public sealed class PlanTheListJourneyTests(PlannerAgentPlanUiFixture agent) : PlanTheListPage(agent)
{
    [Fact]
    public async Task PlanTheList_WithTwoOrdersWaiting_ShowsASuggestionForEachOfThem_AndAssignsNothing()
    {
        var (first, second) = await TwoOrdersWaitingAsync();

        await PlanTheListAsync();

        await Expect(SuggestionFor(first).GetByTestId("suggested-action")).ToContainTextAsync("Assign to");
        await Expect(SuggestionFor(second).GetByTestId("suggested-action")).ToContainTextAsync("Assign to");
        await Expect(Page.GetByTestId("plan-stopped")).ToHaveCountAsync(0);
        await Expect(Outcomes).ToHaveCountAsync(0);
        await Expect(RowOf(first)).ToBeVisibleAsync();
        await Expect(RowOf(second)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Accept_OneSuggestionOfTheList_PreSelectsThatOrderOnly_AndExecutesNothingUntilThePlannerConfirms()
    {
        var (first, second) = await TwoOrdersWaitingAsync();
        await PlanTheListAsync();
        await Expect(SuggestionFor(second).GetByTestId("suggested-action")).ToContainTextAsync("Assign to");

        await SuggestionFor(first).GetByRole(AriaRole.Button, new() { Name = $"Accept the suggestion for {first.Number}" }).ClickAsync();

        await Expect(RowOf(first).GetByRole(AriaRole.Checkbox, new() { Name = $"Select {first.Number}" })).ToBeCheckedAsync();
        await Expect(RowOf(second).GetByRole(AriaRole.Checkbox, new() { Name = $"Select {second.Number}" })).Not.ToBeCheckedAsync();
        await Expect(AssignGroup.GetByRole(AriaRole.Button, new() { Name = "Assign selected (1)" })).ToBeEnabledAsync();
        await Expect(Outcomes).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Ignore_OneSuggestionOfTheList_DropsThatSuggestionOnly_AndSelectsNothing()
    {
        var (first, second) = await TwoOrdersWaitingAsync();
        await PlanTheListAsync();
        await Expect(SuggestionFor(second).GetByTestId("suggested-action")).ToContainTextAsync("Assign to");

        await SuggestionFor(first).GetByRole(AriaRole.Button, new() { Name = $"Ignore the suggestion for {first.Number}" }).ClickAsync();

        await Expect(SuggestionFor(first)).ToHaveCountAsync(0);
        await Expect(SuggestionFor(second)).ToBeVisibleAsync();
        await Expect(AssignGroup.GetByRole(AriaRole.Button, new() { Name = "Assign selected (0)" })).ToBeDisabledAsync();
        await Expect(RowOf(first)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlanTheList_OverItsCap_ShowsTheSuggestionsMadeSoFar_AndSaysWhereItStopped()
    {
        var first = await SubmitAsync();
        var second = await SubmitAsync();
        var third = await SubmitAsync();
        await OpenListAsync(third);

        await PlanTheListAsync();

        var stopped = Page.GetByTestId("plan-stopped");
        await Expect(stopped).ToContainTextAsync("The run stopped at its cap after 2 order(s): 1 order(s), from");
        await Expect(stopped).ToContainTextAsync(new Regex(@"from TO-\d+ on, have no suggestion"));
        await Expect(Page.GetByTestId("plan-suggestion")).ToHaveCountAsync(2);
        await Expect(SuggestionFor(first)).ToBeVisibleAsync();
        await Expect(SuggestionFor(second)).ToBeVisibleAsync();
        await Expect(SuggestionFor(third)).ToHaveCountAsync(0);
        await Expect(stopped).ToContainTextAsync(third.Number);
        await Expect(Outcomes).ToHaveCountAsync(0);
    }

    private async Task<(Order First, Order Second)> TwoOrdersWaitingAsync()
    {
        var first = await SubmitAsync();
        var second = await SubmitAsync();
        await OpenListAsync(second);
        return (first, second);
    }
}
