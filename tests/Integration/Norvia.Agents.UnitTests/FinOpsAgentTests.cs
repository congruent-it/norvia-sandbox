using Microsoft.Extensions.AI;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Agents.Tools;

namespace Norvia.Agents.UnitTests;

/// <summary>The FinOps agent: its definition, its tool list, the scripted model and the runner's narration (ADR 0040, #511).</summary>
public sealed class FinOpsAgentTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.FinOpsDefinition);
    private static readonly AgentQuestion Question = new(FinOpsQuestions.Narrate, "run-1", "agent-run-1");

    private static FakeToolSource Tools(string costView, string risingSpend) =>
        new(new Dictionary<string, Func<string>> { ["get_cost_view"] = () => costView, ["get_rising_spend_findings"] = () => risingSpend });

    private static AgentRunner Runner(IChatClient model, FakeToolSource tools) => new(model, tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)));

    [Fact]
    public void TheDefinition_IsTheOneAgentWithTheCostTools_OfItsOwnServerAndRole()
    {
        var contract = ToolListContract.Parse(Embedded.HealthCheckCostTools);

        Definition.Name.Should().Be("finops");
        Definition.Tools.Server.Should().Be("health-check-cost").And.Be(contract.Server);
        Definition.Tools.Names.Should().Equal("get_cost_view", "get_rising_spend_findings");
        contract.Role.Should().Be("HealthCheck.Cost.Read");
        contract.Tools.Select(tool => tool.Name).Should().Equal(Definition.Tools.Names);
        contract.Tools.Should().OnlyContain(tool => tool.IsRead);
        contract.Unknown(Definition).Should().BeEmpty();
        ToolListContract.Parse(Embedded.HealthCheckTools).Tools.Select(tool => tool.Name).Should().NotIntersectWith(Definition.Tools.Names, "the cost tools stay off the health-check agent");
    }

    [Fact]
    public async Task RunNarrationAsync_ARiseWithFlatVolume_NamesTheResourceAndTheToggle_QuotingOnlyWhatTheToolsReturned()
    {
        var tools = Tools(FinOpsSamples.CostView, FinOpsSamples.RisingSpend);

        var result = await Runner(FinOpsScript.Model(), tools).RunNarrationAsync(Definition, Question, CancellationToken.None);

        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "get_cost_view"), new ToolCallRecord("r2", "get_rising_spend_findings"));
        result.Narration.Summary.Should().Contain("+80%");
        var item = result.Narration.Items.Should().ContainSingle().Subject;
        (item.Id, item.ResultId, item.ToolName).Should().Be(("F-COST-RISING-SPEND", "r2", "get_rising_spend_findings"));
        item.Impact.Should().Contain("log-norvia").And.Contain("0.90 to 4.20 EUR (+367%)").And.Contain("chattyTelemetry")
            .And.Contain("Set chattyTelemetry to false", because: "the fix is the Check's own");
        tools.Opened.Should().Be(new ToolContext("run-1", "X-Correlation-ID"));
    }

    [Fact]
    public async Task RunNarrationAsync_NothingRising_SaysSoAndExplainsNothing()
    {
        var result = await Runner(FinOpsScript.Model(), Tools(FinOpsSamples.CostView, FinOpsSamples.NothingRising)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        result.Narration.Items.Should().BeEmpty();
        result.Narration.Summary.Should().Contain("no rise");
    }

    [Fact]
    public async Task RunNarrationAsync_AnotherQuestion_IsNotAnswered()
    {
        var run = () => Runner(FinOpsScript.Model(), Tools(FinOpsSamples.CostView, FinOpsSamples.RisingSpend)).RunNarrationAsync(Definition, Question with { Text = "Tell me a joke." }, CancellationToken.None);

        await run.Should().ThrowAsync<AgentRunException>();
    }

    [Fact]
    public async Task RunNarrationAsync_AModelThatNarratesAResourceNoCheckFound_IsAgentUnavailable()
    {
        var model = new ScriptedChatClient(context => context.Results.Count switch
        {
            0 => [new FunctionCallContent("c0", "get_cost_view", new Dictionary<string, object?>())],
            1 => [new FunctionCallContent("c1", "get_rising_spend_findings", new Dictionary<string, object?>())],
            _ => [new TextContent("""{"summary":"s","findings":[{"id":"F-MADE-UP","impact":"i","citation":"r2"}]}""")],
        });

        var run = () => Runner(model, Tools(FinOpsSamples.CostView, FinOpsSamples.RisingSpend)).RunNarrationAsync(Definition, Question, CancellationToken.None);

        (await run.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentUnavailable);
    }
}
