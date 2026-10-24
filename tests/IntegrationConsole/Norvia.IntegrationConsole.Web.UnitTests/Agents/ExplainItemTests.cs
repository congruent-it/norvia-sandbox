using Ardalis.Result;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Web.Agents;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests.Agents;

public sealed class ExplainItemTests : ConsoleWebContext
{
    private static readonly Suggestion Answer = new("It <b>failed</b>.", [new Evidence("A step failed.", "r1", "list_failed_runs")], "Fix the step.", Confidence.Medium);

    private int _asked;

    public ExplainItemTests() => Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read).SetPolicies(OperatorSignIn.ReadPolicy);

    private IRenderedComponent<ExplainItem> Render(Func<CancellationToken, Task<Result<Suggestion>>>? ask = null) =>
        Render<ExplainItem>(parameters => parameters
            .Add(component => component.Subject, "the failed billing run")
            .Add(component => component.Ask, ask ?? (_ =>
            {
                _asked++;
                return Task.FromResult(Result.Success(Answer));
            })));

    [Fact]
    public void Render_WhileTheAgentsAreOff_ShowsNothing()
    {
        Render().Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Render_ForAnOperatorWithoutTheReadRole_ShowsNothing()
    {
        TurnAgentsOn();
        Operator.SetRoles("Something.Else").SetPolicies("Something.Else");

        Render().FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhileTheAgentsAreOn_ShowsTheButtonNamedAfterItsSubject_AndAsksNothingUntilItIsClicked()
    {
        TurnAgentsOn();

        var component = Render();

        component.Find("fluent-button").TextContent.Trim().Should().Be("Explain");
        component.Find("fluent-button").GetAttribute("aria-label").Should().Be("Explain the failed billing run");
        _asked.Should().Be(0);
    }

    [Fact]
    public void Explain_Clicked_AsksOnce_AndShowsCauseEvidenceWithCitationsActionAndConfidence_AsTextOnly()
    {
        TurnAgentsOn();
        var component = Render();

        component.Find("fluent-button").Click();

        _asked.Should().Be(1);
        var explanation = component.Find("[data-testid=explanation]");
        explanation.QuerySelectorAll("h2").Select(heading => heading.TextContent).Should().Equal("Cause", "Evidence", "Suggested action");
        explanation.TextContent.Should().Contain("It <b>failed</b>.").And.Contain("[r1, list_failed_runs]").And.Contain("Fix the step.").And.Contain("Confidence: medium");
        component.FindAll("[data-testid=explanation] b").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithAnotherButtonText_ShowsIt()
    {
        TurnAgentsOn();

        var component = Render<ExplainItem>(parameters => parameters
            .Add(component => component.Subject, "the dead-letter queues")
            .Add(component => component.ButtonText, "What needs attention")
            .Add(component => component.Ask, _ => Task.FromResult(Result.Success(Answer))));

        component.Find("fluent-button").TextContent.Trim().Should().Be("What needs attention");
    }

    [Fact]
    public void Explain_WhileTheAgentThinks_SaysSo_AndCantBeClickedAgain()
    {
        TurnAgentsOn();
        var pending = new TaskCompletionSource<Result<Suggestion>>();
        var component = Render(_ => pending.Task);

        component.Find("fluent-button").Click();

        component.Find("[role=status]").TextContent.Should().Contain("The assistant is reading");
        component.Find("fluent-button").HasAttribute("disabled").Should().BeTrue();
        pending.SetResult(Answer);
        component.WaitForAssertion(() => component.FindAll("[data-testid=explanation]").Should().ContainSingle());
    }

    [Theory]
    [InlineData("AgentUnavailable")]
    [InlineData("AgentToolRefused")]
    public void Explain_WhenTheAgentFails_SaysAssistantUnavailable_AndNothingElse(string failureCode)
    {
        TurnAgentsOn();
        var component = Render(_ => Task.FromResult(Result<Suggestion>.Unavailable(failureCode)));

        component.Find("fluent-button").Click();

        component.Find("[role=alert]").TextContent.Replace("warning", "", StringComparison.Ordinal).Trim().Should().Be("Assistant unavailable");
        component.FindAll("[data-testid=explanation]").Should().BeEmpty();
        component.Markup.Should().NotContain(failureCode);
    }

    [Fact]
    public void Explain_WhenTheRequestThrows_SaysAssistantUnavailable()
    {
        TurnAgentsOn();
        var component = Render(_ => throw new InvalidOperationException("boom"));

        component.Find("fluent-button").Click();

        component.Find("[role=alert]").TextContent.Replace("warning", "", StringComparison.Ordinal).Trim().Should().Be("Assistant unavailable");
    }

    [Fact]
    public async Task Dispose_WhileTheAgentThinks_CancelsTheRun()
    {
        TurnAgentsOn();
        CancellationToken seen = default;
        var component = Render(token =>
        {
            seen = token;
            return new TaskCompletionSource<Result<Suggestion>>().Task;
        });
        component.Find("fluent-button").Click();

        await DisposeComponentsAsync();

        seen.IsCancellationRequested.Should().BeTrue();
    }
}
