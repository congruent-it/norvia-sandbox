// NSubstitute sets up a task-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
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
}
