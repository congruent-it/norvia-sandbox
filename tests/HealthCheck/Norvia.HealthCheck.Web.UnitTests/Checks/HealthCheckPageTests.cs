// NSubstitute sets up a task-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Narrate;
using Norvia.HealthCheck.Application.Operators;
using Norvia.HealthCheck.Application.Run;
using Norvia.HealthCheck.Web.Checks;
using Norvia.HealthCheck.Web.Operators;

namespace Norvia.HealthCheck.Web.UnitTests.Checks;

public sealed class HealthCheckPageTests : HealthCheckWebContext
{
    private static readonly Narration Narrated = new(
        "The health check found one problem, in the estate's alerting.",
        [new NarratedItem("F-DLQ-ALERT", "Nobody is told when <i>carrier-tracking</i> dead-letters.", "r1", "run_checks")]);

    public HealthCheckPageTests()
    {
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Run).SetPolicies(OperatorSignIn.RunPolicy);
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(AsFound);
        Requests.NarrateAsync(Arg.Any<NarrateHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Narrated);
    }

    private IRenderedComponent<HealthCheckPage> Render() => Render<HealthCheckPage>();

    [Fact]
    public void Render_ShowsTheButton_AndRunsNothingUntilItIsClicked()
    {
        var page = Render();

        page.Find("fluent-button").TextContent.Trim().Should().Be("Run health check");
        page.FindAll("[data-testid=findings]").Should().BeEmpty();
        Requests.DidNotReceiveWithAnyArgs().RunAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Render_SaysThatLocalFindingsComeFromASnapshot() =>
        Render().Find("[role=note]").TextContent.Should().Contain("snapshot").And.Contain("not from the running estate");

    [Fact]
    public void RunHealthCheck_OnTheAsFoundEstate_ShowsTheFindingWithItsCheckSeverityEvidenceToggleAndFix()
    {
        var page = Render();

        page.Find("fluent-button").Click();

        var finding = page.Find("[data-testid=finding]");
        finding.QuerySelector("[data-testid=finding-id]")!.TextContent.Should().Be("F-DLQ-ALERT");
        finding.QuerySelector("[data-testid=finding-severity]")!.TextContent.Should().Be("High");
        finding.QuerySelector("[data-testid=finding-check]")!.TextContent.Should().Be("DLQ-ALERT");
        finding.QuerySelector("[data-testid=finding-fix]")!.TextContent.Should().Contain("asFound.entitiesWithoutDeadLetterAlert");
        finding.QuerySelectorAll("[data-testid=finding-evidence] li").Select(item => item.TextContent).Should().Contain(text => text.StartsWith("carrier-tracking", StringComparison.Ordinal));
        finding.TextContent.Should().Contain("entitiesWithoutDeadLetterAlert");
        page.FindAll("[data-testid=finding]").Should().ContainSingle();
    }

    [Fact]
    public void RunHealthCheck_WhileItRuns_ListsTheStepsSoFar_TheLastOneStillWorking()
    {
        var running = new TaskCompletionSource<Ardalis.Result.Result<HealthCheckReport>>();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var progress = call.Arg<RunHealthCheckQuery>().Progress!;
            progress.Report("Reading the alert rules");
            progress.Report("Reading the Logic Apps");
            return running.Task;
        });
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() =>
        {
            var steps = page.FindAll("[data-testid=run-step]");
            steps.Select(step => step.TextContent.Trim()).Should().Equal("✓Reading the alert rules", "Reading the Logic Apps");
            steps.Select(step => step.GetAttribute("data-done")).Should().Equal("true", "false");
        });
        running.SetResult(AsFound);
        page.WaitForAssertion(() => page.FindAll("[data-testid=run-step]").Should().BeEmpty());
    }

    [Fact]
    public void RunHealthCheck_ShowsEvidenceAsTextOnly_NeverAsMarkup()
    {
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=finding-evidence] b").Should().BeEmpty();
        page.Find("[data-testid=finding-evidence]").TextContent.Should().Contain("<b>watches</b>");
    }

    [Fact]
    public void RunHealthCheck_OnTheFixedEstate_SaysThereIsNoFinding_AndHowManyChecksPassed()
    {
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Fixed);
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=finding]").Should().BeEmpty();
        page.Find("[data-testid=no-findings]").TextContent.Should().Contain("No Findings").And.Contain("1 Check passed").And.Contain("fixed");
    }

    [Fact]
    public void RunHealthCheck_WithTheAgentsOff_ShowsNoNarration_AndAsksNoAssistant()
    {
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=narration]").Should().BeEmpty();
        page.Markup.Should().NotContain("Assistant unavailable");
        Requests.DidNotReceiveWithAnyArgs().NarrateAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void RunHealthCheck_WithTheAgentsOn_ShowsTheNarration_CitingTheFindingAndTheResult()
    {
        TurnAgentsOn();
        var page = Render();

        page.Find("fluent-button").Click();

        var narration = page.Find("[data-testid=narration]");
        narration.QuerySelector("[data-testid=narration-summary]")!.TextContent.Should().Contain("one problem");
        var item = narration.QuerySelector("[data-testid=narration-item]")!;
        item.TextContent.Should().Contain("F-DLQ-ALERT").And.Contain("Nobody is told").And.Contain("[r1, run_checks]");
        Requests.Received(1).NarrateAsync(Arg.Is<NarrateHealthCheckQuery>(query => query.CorrelationId.Length == 32), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RunHealthCheck_ShowsTheNarrationAsTextOnly_NeverAsMarkup()
    {
        TurnAgentsOn();
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=narration] i").Should().BeEmpty();
        page.Find("[data-testid=narration]").TextContent.Should().Contain("<i>carrier-tracking</i>");
    }

    [Fact]
    public void RunHealthCheck_WithTheAgentsOn_ShowsTheRunId_AndLinksToItsMessageTrailInTheConsole()
    {
        TurnAgentsOn();
        KnowTheConsoleAt("https://console.test/");
        var page = Render();

        page.Find("fluent-button").Click();

        var id = Requests.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<NarrateHealthCheckQuery>().Single().CorrelationId;
        page.Find("[data-testid=run-id] code").TextContent.Should().Be(id);
        page.Find("[data-testid=message-trail]").GetAttribute("href").Should().Be($"https://console.test/?key={id}&within=all");
    }

    [Fact]
    public void RunHealthCheck_WithTheAgentsOn_AndTheConsoleUnknown_ShowsTheRunId_WithoutALink()
    {
        TurnAgentsOn();
        var page = Render();

        page.Find("fluent-button").Click();

        page.Find("[data-testid=run-id] code").TextContent.Should().HaveLength(32);
        page.FindAll("[data-testid=message-trail]").Should().BeEmpty();
    }

    [Fact]
    public void RunHealthCheck_WithAnAssistantThatFails_StillShowsTheRunId_ForItsCallsAreOnItsTrail()
    {
        TurnAgentsOn();
        KnowTheConsoleAt("https://console.test");
        Requests.NarrateAsync(Arg.Any<NarrateHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Narration>.Unavailable(AgentFailureCodes.AgentToolRefused));
        var page = Render();

        page.Find("fluent-button").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable");
        page.Find("[data-testid=message-trail]").GetAttribute("href").Should().StartWith("https://console.test/?key=");
    }

    [Fact]
    public void RunHealthCheck_WithTheAgentsOff_ShowsNoRunId()
    {
        KnowTheConsoleAt("https://console.test");
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=run-id]").Should().BeEmpty();
    }

    [Fact]
    public void RunHealthCheck_WithAnAssistantThatFails_SaysUnavailable_AndTheFindingsStay()
    {
        TurnAgentsOn();
        Requests.NarrateAsync(Arg.Any<NarrateHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Narration>.Unavailable(AgentFailureCodes.AgentToolRefused));
        var page = Render();

        page.Find("fluent-button").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable");
        page.FindAll("[data-testid=narration]").Should().BeEmpty();
        page.FindAll("[data-testid=finding]").Should().ContainSingle();
    }

    [Fact]
    public void RunHealthCheck_WithAnAssistantThatThrows_SaysUnavailable_AndTheFindingsStay()
    {
        TurnAgentsOn();
        Requests.NarrateAsync(Arg.Any<NarrateHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns<Result<Narration>>(_ => throw new InvalidOperationException("boom"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable");
        page.FindAll("[data-testid=finding]").Should().ContainSingle();
    }

    [Fact]
    public void RunHealthCheck_ThatCannotRun_SaysSo_AndAsksNoAssistant()
    {
        TurnAgentsOn();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Result<HealthCheckReport>.Error("the snapshot is gone"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("could not be run");
        page.FindAll("[data-testid=findings]").Should().BeEmpty();
        Requests.DidNotReceiveWithAnyArgs().NarrateAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void RunHealthCheck_ThatThrows_SaysSo_AndAsksNoAssistant()
    {
        TurnAgentsOn();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns<Result<HealthCheckReport>>(_ => throw new InvalidOperationException("boom"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("could not be run");
        Requests.DidNotReceiveWithAnyArgs().NarrateAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void RunHealthCheck_Again_ClearsTheLastResult_AndShowsTheNewOne()
    {
        var page = Render();
        page.Find("fluent-button").Click();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Fixed);

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=finding]").Should().BeEmpty();
        page.FindAll("[data-testid=no-findings]").Should().ContainSingle();
    }

    [Fact]
    public void Dispose_WhileARunIsOpen_CancelsIt()
    {
        var page = Render();

        var dispose = () => page.Instance.Dispose();

        dispose.Should().NotThrow();
    }

    [Fact]
    public void RunHealthCheck_GroupsTheFindingsBySeverity()
    {
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(AsFound with
        {
            Findings = [AsFound.Findings[0] with { Id = "F-LOW", Severity = Severity.Low }, AsFound.Findings[0], AsFound.Findings[0] with { Id = "F-CRIT", Severity = Severity.Critical }],
        });
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=severity-heading]").Select(heading => heading.TextContent).Should().Equal("Critical (1)", "High (1)", "Low (1)");
        page.FindAll("[data-testid=finding-id]").Select(id => id.TextContent).Should().Equal("F-CRIT", "F-DLQ-ALERT", "F-LOW");
    }

    [Fact]
    public void RunHealthCheck_WithTheAgentsOn_ShowsWhyItMattersOnTheFinding_AndExplainsTheOrderingWithoutChangingSeverity()
    {
        TurnAgentsOn();
        var page = Render();

        page.Find("fluent-button").Click();

        page.Find("[data-testid=finding-impact]").TextContent.Should().Contain("Nobody is told").And.Contain("[r1, run_checks]");
        page.Find("[data-testid=ordering-note]").TextContent.Should().Contain("never changes a severity");
        page.Find("[data-testid=finding-severity]").TextContent.Should().Be("High");
    }

    [Fact]
    public void RunHealthCheck_ForTheFirstTime_ShowsNoComparison()
    {
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=comparison]").Should().BeEmpty();
        page.FindAll("[data-testid=finding-status]").Should().BeEmpty();
    }

    [Fact]
    public void RunHealthCheck_AfterFixingTheToggle_ShowsTheFindingAsFixed()
    {
        var page = Render();
        page.Find("fluent-button").Click();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Fixed);

        page.Find("fluent-button").Click();

        page.Find("[data-testid=comparison-summary]").TextContent.Should().Contain("0 new, 1 fixed, 0 unchanged");
        page.Find("[data-testid=fixed-findings]").TextContent.Should().Contain("F-DLQ-ALERT");
    }

    [Fact]
    public void RunHealthCheck_Again_MarksTheFindingsNewOrUnchanged()
    {
        var other = AsFound.Findings[0] with { Id = "F-NEW", CheckId = "NEW" };
        var page = Render();
        page.Find("fluent-button").Click();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(AsFound with { Findings = [AsFound.Findings[0], other] });

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=finding]").Select(card => card.QuerySelector("[data-testid=finding-status]")!.TextContent).Should().Equal("Unchanged", "New");
        page.Find("[data-testid=comparison-summary]").TextContent.Should().Contain("1 new, 0 fixed, 1 unchanged");
    }

    [Fact]
    public void RunHealthCheck_ThatCannotRunTheSecondTime_KeepsThePreviousResultToCompareWith()
    {
        var page = Render();
        page.Find("fluent-button").Click();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Result<HealthCheckReport>.Error("gone"));
        page.Find("fluent-button").Click();
        Requests.RunAsync(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(Fixed);

        page.Find("fluent-button").Click();

        page.Find("[data-testid=comparison-summary]").TextContent.Should().Contain("0 new, 1 fixed");
    }

    [Fact]
    public void ExportAsMarkdown_HandsTheBrowserTheCurrentReport()
    {
        var page = Render();
        page.Find("fluent-button").Click();

        page.Find("[data-testid=export]").Click();

        var call = JSInterop.Invocations["norviaDownloadText"].Should().ContainSingle().Subject;
        call.Arguments[0].Should().Be("estate-health-check-20261025-0900.md");
        call.Arguments[1].Should().BeOfType<string>().Which.Should().Contain("F-DLQ-ALERT").And.Contain("- Check: `DLQ-ALERT`").And.Contain("carrier-tracking").And.Contain("asFound.entitiesWithoutDeadLetterAlert");
    }

    [Fact]
    public void ExportAsMarkdown_IsOfferedOnlyOnceThereIsAReport() =>
        Render().FindAll("[data-testid=export]").Should().BeEmpty();

    [Fact]
    public void FollowUp_WithTheAgentsOff_OffersNoQuestion()
    {
        var page = Render();

        page.Find("fluent-button").Click();

        page.FindAll("[data-testid=ask-why]").Should().BeEmpty();
    }

    [Fact]
    public void FollowUp_AsksTheFixedQuestionAboutThatFinding_AndShowsTheAnswerBesideIt()
    {
        TurnAgentsOn();
        Requests.ExplainAsync(Arg.Any<ExplainFindingQuery>(), Arg.Any<CancellationToken>()).Returns(new NarratedItem("F-DLQ-ALERT", "Fixing it restores the alert.", "r1", "run_checks"));
        var page = Render();
        page.Find("fluent-button").Click();

        page.Find("[data-testid=ask-what-changes]").Click();

        page.Find("[data-testid=follow-up-answer]").TextContent.Should().Contain("What changes if I fix it?").And.Contain("Fixing it restores the alert.");
        Requests.Received(1).ExplainAsync(
            Arg.Is<ExplainFindingQuery>(query => query.FindingId == "F-DLQ-ALERT" && query.Question == FindingQuestion.WhatChangesIfIFixIt && query.CorrelationId.Length == 32),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FollowUp_AskedAgain_ReplacesTheEarlierAnswer()
    {
        TurnAgentsOn();
        Requests.ExplainAsync(Arg.Any<ExplainFindingQuery>(), Arg.Any<CancellationToken>()).Returns(new NarratedItem("F-DLQ-ALERT", "First.", "r1", "run_checks"), new NarratedItem("F-DLQ-ALERT", "Second.", "r1", "run_checks"));
        var page = Render();
        page.Find("fluent-button").Click();

        page.Find("[data-testid=ask-why]").Click();
        page.Find("[data-testid=ask-why]").Click();

        page.Find("[data-testid=follow-up-answer]").TextContent.Should().Contain("Second.").And.NotContain("First.");
    }

    [Fact]
    public void FollowUp_WhenTheAssistantIsUnavailable_SaysSo_AndTheFindingStays()
    {
        TurnAgentsOn();
        Requests.ExplainAsync(Arg.Any<ExplainFindingQuery>(), Arg.Any<CancellationToken>()).Returns(Result<NarratedItem>.Unavailable(AgentFailureCodes.AgentUnavailable));
        var page = Render();
        page.Find("fluent-button").Click();

        page.Find("[data-testid=ask-why]").Click();

        page.Find("[data-testid=finding] [role=alert]").TextContent.Should().Contain("Assistant unavailable");
        page.FindAll("[data-testid=finding]").Should().ContainSingle();
    }

    [Fact]
    public void FollowUp_ShowsTheAnswerAsTextOnly_NeverAsMarkup()
    {
        TurnAgentsOn();
        Requests.ExplainAsync(Arg.Any<ExplainFindingQuery>(), Arg.Any<CancellationToken>()).Returns(new NarratedItem("F-DLQ-ALERT", "See <b>this</b>.", "r1", "run_checks"));
        var page = Render();
        page.Find("fluent-button").Click();

        page.Find("[data-testid=ask-why]").Click();

        page.FindAll("[data-testid=follow-up-answer] b").Should().BeEmpty();
    }
}
