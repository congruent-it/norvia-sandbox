using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents.Explain;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Web.Agents;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests.Agents;

public sealed class ExplainTrailTests : ConsoleWebContext
{
    private const string CorrelationId = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";

    private static readonly Suggestion Answer = new(
        "The Carrier-tracking flow could not map the status code <b>DLV</b>.",
        [new Evidence("The hop ended DeadLettered.", "r1", "get_message_trail"), new Evidence("The run failed at Map.", "r2", "get_run")],
        "Ask the Carrier to resend with POD.",
        Confidence.High);

    public ExplainTrailTests()
    {
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read).SetPolicies(OperatorSignIn.ReadPolicy);
        Explain.ExplainAsync(Arg.Any<ExplainTrailQuery>(), Arg.Any<CancellationToken>()).Returns(Answer);
    }

    private IRenderedComponent<ExplainTrail> Render() => base.Render<ExplainTrail>(parameters => parameters.Add(component => component.CorrelationId, CorrelationId));

    [Fact]
    public void Render_WhileTheAgentsAreOff_ShowsNoButton()
    {
        var component = Render();

        component.FindAll("fluent-button").Should().BeEmpty();
        component.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Render_ForAnOperatorWithoutTheReadRole_ShowsNoButton()
    {
        TurnAgentsOn();
        Operator.SetRoles("Something.Else").SetPolicies("Something.Else");

        var component = Render();

        component.FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhileTheAgentsAreOn_ShowsExplain_AndAsksNothingUntilItIsClicked()
    {
        TurnAgentsOn();

        var component = Render();

        component.Find("fluent-button").TextContent.Trim().Should().Be("Explain");
        Explain.DidNotReceiveWithAnyArgs().ExplainAsync(default(ExplainTrailQuery)!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Explain_Clicked_AsksAboutThatTrail_AndShowsCauseEvidenceWithCitationsActionAndConfidence()
    {
        TurnAgentsOn();
        var component = Render();

        component.Find("fluent-button").Click();

        Explain.Received(1).ExplainAsync(new ExplainTrailQuery(CorrelationId), Arg.Any<CancellationToken>());
        var explanation = component.Find("[data-testid=explanation]");
        explanation.QuerySelectorAll("h2").Select(heading => heading.TextContent).Should().Equal("Cause", "Evidence", "Suggested action");
        explanation.TextContent.Should().Contain("could not map the status code")
            .And.Contain("The hop ended DeadLettered.").And.Contain("[r1, get_message_trail]")
            .And.Contain("The run failed at Map.").And.Contain("[r2, get_run]")
            .And.Contain("Ask the Carrier to resend with POD.")
            .And.Contain("Confidence: high");
    }

    [Fact]
    public void Explain_Answered_ShowsTheAnswerAsTextOnly_NeverAsMarkup()
    {
        TurnAgentsOn();
        var component = Render();

        component.Find("fluent-button").Click();

        component.FindAll("[data-testid=explanation] b").Should().BeEmpty();
        component.Find("[data-testid=explanation]").TextContent.Should().Contain("<b>DLV</b>");
    }

    [Fact]
    public void Explain_WhileTheAgentThinks_SaysSo_AndCantBeClickedAgain()
    {
        TurnAgentsOn();
        var pending = new TaskCompletionSource<Result<Suggestion>>();
        Explain.ExplainAsync(Arg.Any<ExplainTrailQuery>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var component = Render();

        component.Find("fluent-button").Click();

        component.Find("[role=status]").TextContent.Should().Contain("reading the Message trail");
        component.Find("fluent-button").HasAttribute("disabled").Should().BeTrue();
        pending.SetResult(Answer);
        component.WaitForAssertion(() => component.FindAll("[data-testid=explanation]").Should().ContainSingle());
    }

    [Theory]
    [InlineData(AgentFailureCodes.AgentUnavailable)]
    [InlineData(AgentFailureCodes.AgentToolRefused)]
    public void Explain_WhenTheAgentFails_SaysAssistantUnavailable_AndNothingElse(string failureCode)
    {
        TurnAgentsOn();
        Explain.ExplainAsync(Arg.Any<ExplainTrailQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(failureCode));
        var component = Render();

        component.Find("fluent-button").Click();

        component.Find("[role=alert]").TextContent.Replace("warning", "", StringComparison.Ordinal).Trim().Should().Be("Assistant unavailable");
        component.FindAll("[data-testid=explanation]").Should().BeEmpty();
        component.Markup.Should().NotContain(failureCode, because: "the operator sees one message and nothing else");
    }

    [Fact]
    public void Explain_WhenTheRequestThrows_SaysAssistantUnavailable()
    {
        TurnAgentsOn();
        Explain.ExplainAsync(Arg.Any<ExplainTrailQuery>(), Arg.Any<CancellationToken>()).Returns<Task<Result<Suggestion>>>(_ => throw new InvalidOperationException("boom"));
        var component = Render();

        component.Find("fluent-button").Click();

        component.Find("[role=alert]").TextContent.Replace("warning", "", StringComparison.Ordinal).Trim().Should().Be("Assistant unavailable");
    }

    [Fact]
    public void Explain_ClickedAgainAfterAFailure_TriesAgain()
    {
        TurnAgentsOn();
        Explain.ExplainAsync(Arg.Any<ExplainTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable), Answer);
        var component = Render();

        component.Find("fluent-button").Click();
        component.Find("fluent-button").Click();

        component.FindAll("[role=alert]").Should().BeEmpty();
        component.Find("[data-testid=explanation]").Should().NotBeNull();
    }

    [Fact]
    public async Task Dispose_WhileTheAgentThinks_CancelsTheRun()
    {
        TurnAgentsOn();
        CancellationToken seen = default;
        Explain.ExplainAsync(Arg.Any<ExplainTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                seen = call.Arg<CancellationToken>();
                return new TaskCompletionSource<Result<Suggestion>>().Task;
            });
        var component = Render();
        component.Find("fluent-button").Click();

        await DisposeComponentsAsync();

        seen.IsCancellationRequested.Should().BeTrue();
    }
}
