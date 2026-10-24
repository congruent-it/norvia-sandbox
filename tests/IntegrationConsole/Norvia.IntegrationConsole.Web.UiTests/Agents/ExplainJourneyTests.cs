using Ardalis.Result;
using Microsoft.Playwright;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Agents;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Agents;

/// <summary>The Explain journeys with <c>Agents:Enabled</c> on and the Agent scripted: the console's <see cref="IAgentClient"/> port is the fake.</summary>
public sealed class ExplainJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    protected override bool AgentsEnabled => true;

    [Fact]
    public async Task AnOperatorExplainingAFailedTrail_SeesTheCause_TheCitedEvidence_AndASuggestedAction()
    {
        var correlationId = FailedTrail();
        Console.Port<IAgentClient>().AskAsync(Arg.Is<AgentRequest>(request => request.CorrelationId == correlationId), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Suggestion(
                "The driver app was unavailable, so the Shipment update was dead-lettered.",
                [new Evidence("The delivery hop failed with driver-app-unavailable.", "r1", "get_trail")],
                "Resubmit the dead-lettered message once the driver app is back.",
                Confidence.High)));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync($"/trails/{correlationId}");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explain" }).ClickAsync();

        var explanation = Page.GetByTestId("explanation");
        await Expect(explanation).ToContainTextAsync("The driver app was unavailable, so the Shipment update was dead-lettered.");
        await Expect(explanation).ToContainTextAsync("The delivery hop failed with driver-app-unavailable.");
        await Expect(explanation.Locator("cite")).ToHaveTextAsync("[r1, get_trail]");
        await Expect(explanation).ToContainTextAsync("Resubmit the dead-lettered message once the driver app is back.");
        await Expect(explanation).ToContainTextAsync("Confidence: high");
    }

    [Fact]
    public async Task AnOperatorExplainingATrailWhenTheAgentFails_IsToldTheAssistantIsUnavailable_AndTheRestOfThePageWorks()
    {
        var correlationId = FailedTrail();
        Console.Port<IAgentClient>().AskAsync(Arg.Is<AgentRequest>(request => request.CorrelationId == correlationId), Arg.Any<CancellationToken>())
            .Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync($"/trails/{correlationId}");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explain" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(Page.GetByTestId("explanation")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Hops" })).ToContainTextAsync("logic-intake: transport-order-intake");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task AnOperatorWithoutTheReadRole_IsRefused_AndSeesNoExplainButton()
    {
        var correlationId = FailedTrail();
        await SignInAsync("Rita Resubmitter", "IntegrationConsole.Resubmit");

        await Page.GotoAsync($"/trails/{correlationId}");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You can't use the Integration console" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToHaveCountAsync(0);
    }

    private string FailedTrail() => ExplainTrails.Failed(Console);
}
