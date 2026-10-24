using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Agents;

/// <summary>The console as it runs with <c>Agents:Enabled</c> off, which is the default.</summary>
public sealed class ExplainOffJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    [Fact]
    public async Task AnOperatorOpeningAFailedTrailWhileTheAgentsAreOff_SeesNoExplainButton()
    {
        var correlationId = ExplainTrails.Failed(Console);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync($"/trails/{correlationId}");

        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Hops" })).ToContainTextAsync("logic-intake: transport-order-intake");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToHaveCountAsync(0);
    }
}
