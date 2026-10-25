using System.Net;
using System.Text.RegularExpressions;
using Ardalis.Result;
using Microsoft.Playwright;
using Norvia.Agents;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Operators;
using static Microsoft.Playwright.Assertions;

namespace Norvia.HealthCheck.Web.UiTests.Checks;

/// <summary>The run-a-health-check journeys on the snapshots, with the Agents on and the Agent scripted.</summary>
public sealed class RunHealthCheckJourneyTests(HealthCheckUiFixture fixture) : BrowserPage(fixture)
{
    /// <summary>One Finding for each toggle the as-found preset turns on (infra/presets/as-found.json).</summary>
    private const int AsFoundFindings = 13;

    private ILocator FindingOf(string id) =>
        Page.GetByTestId("finding").Filter(new() { Has = Page.GetByTestId("finding-id").Filter(new() { HasTextRegex = new Regex($"^{id}$") }) });

    [Fact]
    public async Task AnOperatorRunningTheHealthCheckOnTheAsFoundSnapshot_SeesTheFindingForEachToggleItTurnsOn_AndTheAssistantsNarration()
    {
        await StartAsync("as-found", OperatorRoles.Run);
        App.Agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Narration(
                "The estate's alerting has a gap, among other problems.",
                [new NarratedItem("F-DLQ-ALERT", "Nobody is told when a dead-letter queue fills.", "r1", "run_checks")])));
        await Page.GotoAsync("/");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Run health check" }).ClickAsync();

        await Expect(Page.GetByTestId("finding")).ToHaveCountAsync(AsFoundFindings);
        await Expect(Page.GetByTestId("finding").First.GetByTestId("finding-severity")).ToHaveTextAsync("Critical");
        var finding = FindingOf("F-DLQ-ALERT");
        await Expect(finding.GetByTestId("finding-id")).ToHaveTextAsync("F-DLQ-ALERT");
        await Expect(finding.GetByTestId("finding-severity")).ToHaveTextAsync("High");
        await Expect(finding.GetByTestId("finding-fix")).ToContainTextAsync("entitiesWithoutDeadLetterAlert");
        var narration = Page.GetByTestId("narration");
        await Expect(narration.GetByTestId("narration-summary")).ToHaveTextAsync("The estate's alerting has a gap, among other problems.");
        await Expect(narration.GetByTestId("narration-item")).ToContainTextAsync("Nobody is told when a dead-letter queue fills.");
        await Expect(narration.Locator("cite")).ToHaveTextAsync("[r1, run_checks]");
    }

    [Fact]
    public async Task AnOperatorRunningTheHealthCheckOnTheFixedSnapshot_SeesNoFinding()
    {
        await StartAsync("fixed", OperatorRoles.Run);
        App.Agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Narration("Nothing to report.", [])));
        await Page.GotoAsync("/");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Run health check" }).ClickAsync();

        await Expect(Page.GetByTestId("no-findings")).ToContainTextAsync("No Findings");
        await Expect(Page.GetByTestId("finding")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnOperatorWithoutTheRunRole_IsRefused_AndSeesNoHealthCheck()
    {
        await StartAsync("as-found", OperatorRoles.Read);

        var response = await Page.GotoAsync("/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You can't run an Estate health check" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Run health check" })).ToHaveCountAsync(0);
        response!.Status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnOperatorRunningTheHealthCheckWhenTheAgentFails_IsToldTheAssistantIsUnavailable_AndTheFindingsStillShow()
    {
        await StartAsync("as-found", OperatorRoles.Run);
        App.Agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<Narration>.Unavailable("AgentUnavailable"));
        await Page.GotoAsync("/");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Run health check" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(Page.GetByTestId("narration")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("finding-id").Filter(new() { HasTextString = "F-DLQ-ALERT" })).ToHaveCountAsync(1);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Run health check" })).ToBeEnabledAsync();
    }
}
