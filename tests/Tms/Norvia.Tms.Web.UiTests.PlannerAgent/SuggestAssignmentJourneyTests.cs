using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// The Planner asks the assistant who should carry a row of the waiting-for-planning list, against the scripted fake model: an order on a day Own fleet can carry, and an order on the demo
/// seed's Over capacity day, where Own fleet would carry a warning. The answer is shown only: the order is still on the list, assigned to no one.
/// </summary>
public sealed class SuggestAssignmentJourneyTests(PlannerAgentUiFixture agent) : WaitingForPlanningPage(agent)
{
    [Fact]
    public async Task Suggest_OnAnOrderThatFitsOwnFleet_RecommendsOwnFleetWithNoWarning_AndAssignsNothing()
    {
        var shipperReference = NewReference();
        var number = await SubmitAcceptedAsync(BookingOn(shipperReference, await DayThatFitsAsync(TestContext.Current.CancellationToken)));
        await OpenListAsync("Planner", shipperReference);
        var row = RowOf(shipperReference);

        await row.GetByRole(AriaRole.Button, new() { Name = $"Suggest for {number}" }).ClickAsync();

        var suggestion = row.GetByTestId("suggestion");
        await Expect(suggestion.GetByRole(AriaRole.Heading, new() { Name = "Suggestion" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(suggestion.GetByTestId("suggested-action")).ToContainTextAsync("Assign to Own fleet");
        await Expect(suggestion).ToContainTextAsync("it fits the collection day with no warning");
        await Expect(suggestion.GetByRole(AriaRole.Heading, new() { Name = "Evidence" })).ToBeVisibleAsync();
        await Expect(suggestion.GetByRole(AriaRole.Listitem)).ToContainTextAsync(["Own fleet fits the collection day"]);
        await Expect(suggestion.GetByRole(AriaRole.Listitem).First).ToContainTextAsync("get_assignment_options");
        await Expect(suggestion).ToContainTextAsync("nothing is assigned until you do");

        // It recommends and leaves the choice to the Planner: the order is still waiting, unselected, and no outcome was announced.
        await Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" })).Not.ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" })).ToHaveCountAsync(0);
        await Expect(row).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Suggest_OnAnOrderOnTheOverCapacityDay_NamesTheWarningAndTheCheapestCarriersCost_AndAssignsNothing()
    {
        var shipperReference = NewReference();
        var number = await SubmitAcceptedAsync(BookingOn(shipperReference, await OverCapacityDayAsync(TestContext.Current.CancellationToken)));
        await OpenListAsync("Planner", shipperReference);
        var row = RowOf(shipperReference);

        await row.GetByRole(AriaRole.Button, new() { Name = $"Suggest for {number}" }).ClickAsync();

        var suggestion = row.GetByTestId("suggestion");
        await Expect(suggestion.GetByRole(AriaRole.Heading, new() { Name = "Suggestion" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        var action = suggestion.GetByTestId("suggested-action");
        await Expect(action).ToContainTextAsync(new Regex(@"Assign to Carrier .+ at \d+\.\d{2} EUR"));
        await Expect(action).ToContainTextAsync("Own fleet would carry a warning");
        await Expect(action).ToContainTextAsync("Over capacity");
        await Expect(suggestion.GetByRole(AriaRole.Listitem)).ToContainTextAsync([new Regex(@"prices it at \d+\.\d{2} EUR, the lowest Carrier cost"), new Regex("Own fleet: OverCapacity")]);
        await Expect(suggestion).ToContainTextAsync("you decide");

        await Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" })).Not.ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" })).ToHaveCountAsync(0);
        await Expect(row).ToBeVisibleAsync();
    }
}
