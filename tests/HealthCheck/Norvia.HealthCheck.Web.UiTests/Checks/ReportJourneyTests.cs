using Ardalis.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Operators;
using Norvia.HealthCheck.Infrastructure.Estate;
using static Microsoft.Playwright.Assertions;

namespace Norvia.HealthCheck.Web.UiTests.Checks;

/// <summary>The report journeys on the snapshots, with the Agents on and the Agent scripted.</summary>
public sealed class ReportJourneyTests(HealthCheckUiFixture fixture) : BrowserPage(fixture)
{
    private static readonly string[] SeverityOrder = ["Critical", "High", "Medium", "Low"];

    private async Task StartWithNothingToSayAsync(string snapshot)
    {
        await StartAsync(snapshot, OperatorRoles.Run);
        App.Agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Narration("Nothing more to say.", [])));
        await Page.GotoAsync("/");
    }

    private ILocator RunButton => Page.GetByRole(AriaRole.Button, new() { Name = "Run health check" });

    [Fact]
    public async Task AnOperatorRunningTheHealthCheckOnTheAsFoundSnapshot_SeesTheFindingsGroupedBySeverity_MostSevereFirst()
    {
        await StartWithNothingToSayAsync("as-found");

        await RunButton.ClickAsync();

        var groups = Page.GetByTestId("severity-group");
        await Expect(groups.First).ToBeVisibleAsync();
        var headings = await Page.GetByTestId("severity-heading").AllTextContentsAsync();
        var severities = headings.Select(heading => heading.Split(' ')[0]).ToList();
        severities.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
        severities.Should().BeInAscendingOrder(Comparer<string>.Create((a, b) => Array.IndexOf(SeverityOrder, a).CompareTo(Array.IndexOf(SeverityOrder, b))));
        for (var index = 0; index < severities.Count; index++)
        {
            var group = groups.Nth(index);
            var findingSeverities = await group.GetByTestId("finding-severity").AllTextContentsAsync();
            findingSeverities.Should().NotBeEmpty().And.OnlyContain(severity => severity == severities[index]);
        }
    }

    [Fact]
    public async Task AnOperatorFixingTheEstateAndRunningAgain_SeesEveryFindingAsFixed()
    {
        await StartWithNothingToSayAsync("as-found");
        await RunButton.ClickAsync();
        await Expect(Page.GetByTestId("finding").First).ToBeVisibleAsync();
        var before = await Page.GetByTestId("finding-id").AllInnerTextsAsync();

        // The operator fixes the toggles: the app reads the fixed snapshot from now on.
        App.Services.GetRequiredService<IOptions<SnapshotOptions>>().Value.Snapshot = SnapshotOptions.Fixed;
        await RunButton.ClickAsync();

        await Expect(Page.GetByTestId("no-findings")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("comparison-summary")).ToHaveTextAsync($"0 new, {before.Count} fixed, 0 unchanged.");
        var fixedIds = await Page.GetByTestId("fixed-findings").Locator("strong").AllInnerTextsAsync();
        fixedIds.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task AnOperatorRunningAgainWithNothingFixed_SeesEveryFindingAsUnchanged()
    {
        await StartWithNothingToSayAsync("as-found");
        await RunButton.ClickAsync();
        await Expect(Page.GetByTestId("finding").First).ToBeVisibleAsync();
        var count = await Page.GetByTestId("finding").CountAsync();

        await RunButton.ClickAsync();

        await Expect(Page.GetByTestId("comparison-summary")).ToHaveTextAsync($"0 new, 0 fixed, {count} unchanged.");
        await Expect(Page.GetByTestId("finding-status")).ToHaveCountAsync(count);
        await Expect(Page.GetByTestId("finding-status").First).ToHaveTextAsync("Unchanged");
    }

    [Fact]
    public async Task AnOperatorExportingTheReport_DownloadsMarkdownWithEveryFindingItsCheckIdEvidenceAndFix()
    {
        await StartWithNothingToSayAsync("as-found");
        await RunButton.ClickAsync();
        await Expect(Page.GetByTestId("finding").First).ToBeVisibleAsync();
        var ids = await Page.GetByTestId("finding-id").AllInnerTextsAsync();
        var checkIds = await Page.GetByTestId("finding-check").AllInnerTextsAsync();
        var fixes = await Page.GetByTestId("finding-fix").AllInnerTextsAsync();

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.GetByTestId("export").ClickAsync());

        download.SuggestedFilename.Should().MatchRegex(@"^estate-health-check-\d{8}-\d{4}\.md$");
        var path = await download.PathAsync();
        var markdown = await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken);
        markdown.Should().Contain("- Evidence:");
        for (var index = 0; index < ids.Count; index++)
        {
            markdown.Should().Contain($"### {ids[index]}:").And.Contain($"- Check: `{checkIds[index]}`").And.Contain($"- Fix: {fixes[index].Trim()}");
        }
    }

    [Fact]
    public async Task AnOperatorAskingWhyAFindingIsAProblem_SeesTheAssistantsAnswerUnderThatFindingAlone()
    {
        await StartWithNothingToSayAsync("as-found");
        App.Agent.NarrateAsync(
                Arg.Is<AgentRequest>(request => request.Question == HealthCheckQuestions.Why("F-DLQ-ALERT")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Narration(
                "Why it is a problem.",
                [new NarratedItem("F-DLQ-ALERT", "A dead-letter queue can fill without anyone knowing.", "r2", "run_checks")])));
        await RunButton.ClickAsync();
        var finding = Page.GetByTestId("finding").Filter(new() { Has = Page.GetByText("F-DLQ-ALERT", new() { Exact = true }) });

        await finding.GetByTestId("ask-why").ClickAsync();

        var answer = finding.GetByTestId("follow-up-answer");
        await Expect(answer).ToContainTextAsync("A dead-letter queue can fill without anyone knowing.");
        await Expect(answer).ToContainTextAsync("Why is this a problem?");
        await Expect(answer.Locator("cite")).ToHaveTextAsync("[r2, run_checks]");
        await Expect(Page.GetByTestId("follow-up-answer")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task AnOperatorAskingAboutAFindingWhenTheAgentFails_IsToldTheAssistantIsUnavailable_AndTheFindingStays()
    {
        await StartWithNothingToSayAsync("as-found");
        App.Agent.NarrateAsync(
                Arg.Is<AgentRequest>(request => request.Question == HealthCheckQuestions.WhatChanges("F-DLQ-ALERT")),
                Arg.Any<CancellationToken>())
            .Returns(Result<Narration>.Unavailable("AgentUnavailable"));
        await RunButton.ClickAsync();
        var finding = Page.GetByTestId("finding").Filter(new() { Has = Page.GetByText("F-DLQ-ALERT", new() { Exact = true }) });

        await finding.GetByTestId("ask-what-changes").ClickAsync();

        await Expect(finding.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(finding.GetByTestId("follow-up-answer")).ToHaveCountAsync(0);
        await Expect(finding.GetByTestId("finding-fix")).ToBeVisibleAsync();
    }
}
