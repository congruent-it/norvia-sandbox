using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// An Agent that fails costs the list nothing but the suggestion. It stops the MCP stand-in, which Aspire cannot stop once it has served calls, so <see cref="StopsTheAgentFirst"/> runs
/// this class before the others.
/// </summary>
public sealed class SuggestAssignmentFailureJourneyTests(PlannerAgentUiFixture agent) : WaitingForPlanningPage(agent)
{
    [Fact]
    public async Task Suggest_WhenTheAgentFails_ShowsAssistantUnavailableAndTheRestOfTheListWorks()
    {
        var shipperReference = NewReference();
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));
        await OpenListAsync("Planner", shipperReference);
        var row = RowOf(shipperReference);

        await Agent.StopAgentAsync();
        try
        {
            await row.GetByRole(AriaRole.Button, new() { Name = $"Suggest for {number}" }).ClickAsync();

            await Expect(row.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable", new() { Timeout = AgentTimeout });
            await Expect(row.GetByTestId("suggestion")).ToHaveCountAsync(0);
            await Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" })).ToBeEnabledAsync();
            await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Suggest for {number}" })).ToBeEnabledAsync();
        }
        finally
        {
            await Agent.StartAgentAsync();
        }
    }
}
