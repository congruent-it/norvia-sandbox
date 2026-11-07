using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// An Agent that fails costs the Transport Order page nothing but the explanation. It stops the MCP stand-in, which Aspire cannot stop once <see cref="ExplainTransportOrderJourneyTests"/>
/// has made its several tool calls through it, so <see cref="StopsTheAgentFirst"/> runs this class before that one.
/// </summary>
public sealed class ExplainTransportOrderFailureJourneyTests(CustomerServiceAgentUiFixture fixture) : TransportOrderPage(fixture)
{
    private const float AgentTimeout = 30_000;

    [Fact]
    public async Task Explain_WhenTheAgentFails_ShowsAssistantUnavailableAndTheRestOfThePageWorks()
    {
        var number = await SubmitAcceptedAsync(Bookings.Valid($"UI-{Guid.NewGuid():N}"[..20]));
        await OpenTransportOrderAsync(number);

        await Agent.StopAgentAsync();
        try
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton }).ClickAsync();

            await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable", new() { Timeout = AgentTimeout });
            await Expect(Page.GetByTestId("explanation")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton })).ToBeEnabledAsync();
        }
        finally
        {
            await Agent.StartAgentAsync();
        }
    }
}
