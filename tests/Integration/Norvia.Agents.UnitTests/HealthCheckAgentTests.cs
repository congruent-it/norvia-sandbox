using Microsoft.Extensions.AI;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Agents.Tools;

namespace Norvia.Agents.UnitTests;

/// <summary>The Estate health-check agent: its definition, its tool list, the scripted model and the runner's narration (ADR 0040, #371).</summary>
public sealed class HealthCheckAgentTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.EstateHealthCheckDefinition);
    private static readonly AgentQuestion Question = new(HealthCheckQuestions.Narrate, "run-1", "agent-run-1");

    private static AgentRunner Runner(IChatClient model, FakeToolSource tools) => new(model, tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)));

    private static FakeToolSource Tools(string answer) => new(new Dictionary<string, Func<string>> { ["run_checks"] = () => answer });

    [Fact]
    public void TheDefinition_IsTheOneAgentWithTheOneReadTool_OfTheHealthCheckServer()
    {
        var contract = ToolListContract.Parse(Embedded.HealthCheckTools);

        Definition.Name.Should().Be("estate-health-check");
        Definition.Tools.Server.Should().Be("health-check").And.Be(contract.Server);
        Definition.Tools.Names.Should().Equal("run_checks");
        contract.Role.Should().Be("HealthCheck.Agent.Read");
        contract.Tools.Should().ContainSingle().Which.IsRead.Should().BeTrue();
        contract.Unknown(Definition).Should().BeEmpty();
        Definition.Model.MaxToolCalls.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RunNarrationAsync_OneFinding_NarratesItCitingTheResult()
    {
        var tools = Tools(HealthCheckSamples.OneFinding);

        var result = await Runner(HealthCheckScript.Model(), tools).RunNarrationAsync(Definition, Question, CancellationToken.None);

        result.RunId.Should().Be("agent-run-1");
        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "run_checks"));
        result.Narration.Summary.Should().Contain("one problem");
        var item = result.Narration.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be("F-DLQ-ALERT");
        item.ResultId.Should().Be("r1");
        item.ToolName.Should().Be("run_checks");
        item.Impact.Should().Contain("nobody is told", because: "that is what a missing dead-letter alert means").And.Contain("Carrier-tracking incident", because: "the evidence names carrier-tracking");
        tools.Opened.Should().Be(new ToolContext("run-1", "X-Correlation-ID"));
        tools.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task RunNarrationAsync_AFindingNotAboutCarrierTracking_DoesNotLinkItToTheIncident()
    {
        var result = await Runner(HealthCheckScript.Model(), Tools(HealthCheckSamples.NoCarrierTracking)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        result.Narration.Items.Single().Impact.Should().NotContain("Carrier-tracking incident").And.Contain("billing");
    }

    [Fact]
    public async Task RunNarrationAsync_NoFinding_SaysSo()
    {
        var result = await Runner(HealthCheckScript.Model(), Tools(HealthCheckSamples.NoFinding)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        result.Narration.Items.Should().BeEmpty();
        result.Narration.Summary.Should().Contain("found nothing");
    }

    [Fact]
    public async Task RunNarrationAsync_SeveralFindings_OrdersThemBySeverity_MostSevereFirst()
    {
        var result = await Runner(HealthCheckScript.Model(), Tools(HealthCheckSamples.TwoChecks)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        result.Narration.Items.Select(item => item.Id).Should().Equal("F-CRIT-ONE", "F-LOW-ONE");
        result.Narration.Summary.Should().Contain("2 problems");
        result.Narration.Items[0].Impact.Should().Contain("Something big").And.Contain("A big rule.");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"findings":"none"}""")]
    public async Task RunNarrationAsync_AResultThatIsNoFindingList_IsNarratedAsNothingFound(string answer)
    {
        var result = await Runner(HealthCheckScript.Model(), Tools(answer)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        result.Narration.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task RunNarrationAsync_AnotherQuestion_IsNotAnswered()
    {
        var run = () => Runner(HealthCheckScript.Model(), Tools(HealthCheckSamples.OneFinding)).RunNarrationAsync(Definition, Question with { Text = "Tell me a joke." }, CancellationToken.None);

        await run.Should().ThrowAsync<AgentRunException>();
    }

    [Fact]
    public async Task RunNarrationAsync_AModelThatInventsAFinding_IsAgentUnavailable()
    {
        var model = new ScriptedChatClient(context => context.Results.Count == 0
            ? [new FunctionCallContent("call-0", "run_checks", new Dictionary<string, object?>())]
            : [new TextContent("""{"summary":"s","findings":[{"id":"F-MADE-UP","impact":"i","citation":"r1"}]}""")]);

        var run = () => Runner(model, Tools(HealthCheckSamples.OneFinding)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        (await run.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task RunNarrationAsync_AModelThatAsksForAToolItLacks_IsAgentToolRefused()
    {
        var model = new ScriptedChatClient(_ => [new FunctionCallContent("call-0", "delete_estate", new Dictionary<string, object?>())]);

        var run = () => Runner(model, Tools(HealthCheckSamples.OneFinding)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        (await run.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task RunNarrationAsync_ARefusedTool_IsAgentToolRefused()
    {
        var tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["run_checks"] = () => throw new InvalidOperationException("403") });

        var run = () => Runner(HealthCheckScript.Model(), tools).RunNarrationAsync(Definition, Question, CancellationToken.None);

        (await run.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public void TheQuestion_IsOneFixedText()
    {
        HealthCheckQuestions.IsNarrate(HealthCheckQuestions.Narrate).Should().BeTrue();
        HealthCheckQuestions.IsNarrate("anything else").Should().BeFalse();
    }
}
