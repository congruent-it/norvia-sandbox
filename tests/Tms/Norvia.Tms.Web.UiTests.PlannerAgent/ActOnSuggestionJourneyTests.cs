using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// The Planner acts on a Suggestion in the assign flow, against the scripted fake model. The Suggestion pre-selects the order and who carries it and executes nothing; the Planner confirms with
/// the button they always press, may choose otherwise, and sees a refusal as ever. Which Agent run the trace names is read in the integration tests, not here.
/// </summary>
public sealed class ActOnSuggestionJourneyTests(PlannerAgentUiFixture agent) : WaitingForPlanningPage(agent)
{
    [Fact]
    public async Task Suggest_OnAnOrderThatFitsOwnFleet_PreSelectsTheOrderAndOwnFleet_AndExecutesNothingUntilThePlannerConfirms()
    {
        var shipperReference = NewReference();
        var number = await SubmitAcceptedAsync(BookingOn(shipperReference, await DayThatFitsAsync(TestContext.Current.CancellationToken)));
        await OpenListAsync("Planner", shipperReference);
        var row = RowOf(shipperReference);
        await Expect(AssignGroup.GetByRole(AriaRole.Button, new() { Name = "Assign selected (0)" })).ToBeDisabledAsync();

        await SuggestAsync(row, number);

        await Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" })).ToBeCheckedAsync();
        await Expect(AssignGroup.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" })).ToContainTextAsync("Own fleet");
        await Expect(AssignGroup.GetByRole(AriaRole.Button, new() { Name = "Assign selected (1)" })).ToBeEnabledAsync();
        await Expect(Outcomes).ToHaveCountAsync(0);
        await Expect(row).ToBeVisibleAsync();

        await AssignGroup.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();

        await Expect(Outcomes.GetByRole(AriaRole.Status)).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        await Expect(Outcomes.GetByRole(AriaRole.Status)).ToContainTextAsync("carried by Own fleet");
    }

    [Fact]
    public async Task Suggest_OnTheOverCapacityDay_PreSelectsTheCarrier_AndOwnFleetStillCarriesItsWarningWhenThePlannerChoosesIt()
    {
        var shipperReference = NewReference();
        var number = await SubmitAcceptedAsync(BookingOn(shipperReference, await OverCapacityDayAsync(TestContext.Current.CancellationToken)));
        await OpenListAsync("Planner", shipperReference);
        var row = RowOf(shipperReference);

        var carrier = await SuggestedCarrierAsync(await SuggestAsync(row, number));

        await Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" })).ToBeCheckedAsync();
        carrier.Should().NotBeNullOrWhiteSpace("the Suggestion names the Carrier");
        var picker = AssignGroup.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" });
        await Expect(picker).ToContainTextAsync(carrier);
        await Expect(Outcomes).ToHaveCountAsync(0);

        // The pre-selection is the Planner's to change: Own fleet, as ever, assigns with its load-check warning.
        await picker.ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Own fleet" }).ClickAsync();
        await AssignGroup.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();

        var outcome = Outcomes.GetByRole(AriaRole.Status);
        await Expect(outcome).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        await Expect(outcome).ToContainTextAsync("carried by Own fleet");
        await Expect(outcome).ToContainTextAsync("Over capacity");
    }

    [Fact]
    public async Task Confirm_AfterTheOrderWasPlannedMeanwhile_ShowsTheRefusalAsUsual_AndMarksTheSuggestionOutOfDate()
    {
        var shipperReference = NewReference();
        var number = await SubmitAcceptedAsync(BookingOn(shipperReference, await DayThatFitsAsync(TestContext.Current.CancellationToken)));
        await OpenListAsync("Planner", shipperReference);
        var row = RowOf(shipperReference);
        var suggestion = await SuggestAsync(row, number);
        await Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" })).ToBeCheckedAsync();
        await AssignBehindThePageAsync(number, TestContext.Current.CancellationToken);

        await AssignGroup.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();

        await Expect(Outcomes.GetByRole(AriaRole.Alert)).ToContainTextAsync($"Transport Order {number} wasn't assigned");
        await Expect(suggestion.GetByRole(AriaRole.Alert)).ToContainTextAsync("This suggestion is out of date");
    }
}
