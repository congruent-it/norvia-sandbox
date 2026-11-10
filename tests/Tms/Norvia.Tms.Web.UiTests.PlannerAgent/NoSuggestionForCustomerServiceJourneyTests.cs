using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>Suggesting who carries an order is the Planner's. Customer service sees the same list, with the Agents on, and no Suggest button.</summary>
public sealed class NoSuggestionForCustomerServiceJourneyTests(PlannerAgentUiFixture agent) : WaitingForPlanningPage(agent)
{
    [Fact]
    public async Task TheList_InTheCustomerServiceView_HasNoSuggestButton()
    {
        var shipperReference = NewReference();
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference));

        await OpenListAsync("Customer service", shipperReference);

        await Expect(RowOf(shipperReference).GetByRole(AriaRole.Link, new() { Name = number })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Suggest", Exact = false })).ToHaveCountAsync(0);
    }
}
