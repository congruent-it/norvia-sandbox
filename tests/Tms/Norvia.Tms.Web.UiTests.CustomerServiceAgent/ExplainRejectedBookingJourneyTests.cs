using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// Customer service asks the assistant to explain a Rejected booking, against the scripted fake model. The answer is plain language with the evidence it cites and a suggested action;
/// the button shows only in Customer service's view; and an Agent that fails costs the page nothing but the explanation.
/// </summary>
public sealed class ExplainRejectedBookingJourneyTests(CustomerServiceAgentUiFixture agent) : BrowserPage(agent)
{
    // The assistant reads the booking through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    private const float AgentTimeout = 30_000;

    [Fact]
    public async Task Explain_OnARejectedBooking_ShowsTheReasonsInPlainLanguageCitedWithASuggestedAction()
    {
        var shipperReference = await RejectWithoutGoodsAsync();

        await OpenRejectedBookingAsync(shipperReference, "Customer service");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Explain this rejected booking" }).ClickAsync();

        var explanation = Page.GetByTestId("explanation");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Why it was rejected" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync("The booking has no goods.");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Evidence" })).ToBeVisibleAsync();
        await Expect(explanation.GetByRole(AriaRole.Listitem)).ToContainTextAsync(["The booking was rejected with GoodsLinesMissing: The booking has no goods lines."]);
        await Expect(explanation.Locator("cite")).ToContainTextAsync("get_rejected_booking");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "What the Shipper should change" })).ToBeVisibleAsync();
        await Expect(explanation).ToContainTextAsync("Add at least one goods line.");
        await Expect(explanation).ToContainTextAsync("you decide");
    }

    [Fact]
    public async Task Explain_InThePlannerView_IsNotThere()
    {
        var shipperReference = await RejectWithoutGoodsAsync();

        await OpenRejectedBookingAsync(shipperReference, "Customer service");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain this rejected booking" })).ToBeVisibleAsync();
        var address = new Uri(Page.Url).AbsolutePath;

        // The page is open to anyone who knows its address, but the assistant is Customer service's.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await Page.GotoAsync(address);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Rejected booking {shipperReference}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain this rejected booking" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Explain_WhenTheAgentFails_ShowsAssistantUnavailableAndTheRestOfThePageWorks()
    {
        var shipperReference = await RejectWithoutGoodsAsync();
        await OpenRejectedBookingAsync(shipperReference, "Customer service");

        await agent.StopAgentAsync();
        try
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Explain this rejected booking" }).ClickAsync();

            await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable", new() { Timeout = AgentTimeout });
            await Expect(Page.GetByTestId("explanation")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Reasons" }).GetByRole(AriaRole.Listitem)).ToHaveTextAsync(["The booking has no goods lines."]);
            await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Goods" })).ToContainTextAsync("No goods lines were given.");
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain this rejected booking" })).ToBeEnabledAsync();
        }
        finally
        {
            await agent.StartAgentAsync();
        }
    }

    private async Task<string> RejectWithoutGoodsAsync()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var booking = Bookings.Valid(shipperReference);
        booking["goodsLines"] = new JsonArray();
        using var response = await agent.SubmitAsync(booking, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return shipperReference;
    }

    private async Task OpenRejectedBookingAsync(string shipperReference, string role)
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = role }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Rejected bookings", Level = 1 })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference }).GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Rejected booking {shipperReference}", Level = 1 })).ToBeVisibleAsync();
    }
}
