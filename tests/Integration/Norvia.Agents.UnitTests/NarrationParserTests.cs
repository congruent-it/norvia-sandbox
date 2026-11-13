using Norvia.Agents.Runner;

namespace Norvia.Agents.UnitTests;

public sealed class NarrationParserTests
{
    private static readonly Dictionary<string, ToolResultText> Results = new() { ["r1"] = new ToolResultText("run_checks", HealthCheckSamples.OneFinding) };

    private const string Good = """{"summary":"One problem.","findings":[{"id":"F-DLQ-ALERT","impact":"Nobody is told.","citation":"r1"}]}""";

    [Fact]
    public void Parse_AGoodReply_ReadsTheSummaryAndTheFindingsWithTheirResult()
    {
        var narration = NarrationParser.Parse(Good, Results);

        narration.Summary.Should().Be("One problem.");
        narration.Items.Should().Equal(new NarratedItem("F-DLQ-ALERT", "Nobody is told.", "r1", "run_checks"));
        narration.Run.Should().BeNull();
    }

    [Fact]
    public void Parse_AReplyInACodeFence_ReadsIt() =>
        NarrationParser.Parse($"```json\n{Good}\n```", Results).Items.Should().ContainSingle();

    [Fact]
    public void Parse_NothingToExplain_IsAnEmptyList() =>
        NarrationParser.Parse("""{"summary":"Nothing found.","findings":[]}""", new Dictionary<string, ToolResultText>()).Items.Should().BeEmpty();

    [Fact]
    public void Parse_AFindingTheResultDoesNotHold_IsRefused_BecauseTheAgentNeverInventsOne()
    {
        var parse = () => NarrationParser.Parse("""{"summary":"s","findings":[{"id":"F-MADE-UP","impact":"i","citation":"r1"}]}""", Results);

        parse.Should().Throw<AgentRunException>().WithMessage("*F-MADE-UP*does not hold*").Which.FailureCode.Should().Be(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public void Parse_ACitationOfAResultTheRunNeverHad_IsRefused()
    {
        var parse = () => NarrationParser.Parse("""{"summary":"s","findings":[{"id":"F-DLQ-ALERT","impact":"i","citation":"r9"}]}""", Results);

        parse.Should().Throw<AgentRunException>().WithMessage("*r9*");
    }

    [Fact]
    public void Parse_TheSameFindingTwice_IsRefused()
    {
        var parse = () => NarrationParser.Parse("""{"summary":"s","findings":[{"id":"F-DLQ-ALERT","impact":"i","citation":"r1"},{"id":"F-DLQ-ALERT","impact":"j","citation":"r1"}]}""", Results);

        parse.Should().Throw<AgentRunException>().WithMessage("*twice*");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"findings":[]}""")]
    [InlineData("""{"summary":"s"}""")]
    [InlineData("""{"summary":"s","findings":"none"}""")]
    [InlineData("""{"summary":"s","findings":["F-DLQ-ALERT"]}""")]
    [InlineData("""{"summary":"s","findings":[{"id":"F-DLQ-ALERT","citation":"r1"}]}""")]
    [InlineData("""{"summary":"s","findings":[{"impact":"i","citation":"r1"}]}""")]
    [InlineData("""{"summary":"s","findings":[{"id":"F-DLQ-ALERT","impact":"i"}]}""")]
    [InlineData("""{"summary":" ","findings":[]}""")]
    public void Parse_AReplyThatIsNotANarration_IsRefused(string reply)
    {
        var parse = () => NarrationParser.Parse(reply, Results);

        parse.Should().Throw<AgentRunException>();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"findings":"none"}""")]
    [InlineData("""["F-DLQ-ALERT"]""")]
    [InlineData("""{"findings":[{"id":1},{"title":"no id"}]}""")]
    public void Parse_AResultThatHoldsNoFindingIds_BacksNoFinding(string content)
    {
        var parse = () => NarrationParser.Parse(Good, new Dictionary<string, ToolResultText> { ["r1"] = new ToolResultText("run_checks", content) });

        parse.Should().Throw<AgentRunException>().WithMessage("*does not hold*");
    }

    [Fact]
    public void Parse_NullArguments_Throw()
    {
        var noReply = () => NarrationParser.Parse(null!, Results);
        var noResults = () => NarrationParser.Parse(Good, null!);

        noReply.Should().Throw<ArgumentNullException>();
        noResults.Should().Throw<ArgumentNullException>();
    }

    private static readonly Dictionary<string, ToolResultText> CostResults = new()
    {
        ["r1"] = new ToolResultText("get_cost_view", """{"periodDays":7,"total":{"previousEurPerDay":4.10,"currentEurPerDay":7.4,"change":"+80.5%"}}"""),
        ["r2"] = new ToolResultText("get_rising_spend_findings", """{"findings":[{"id":"F-COST","evidence":[{"subject":"log-1","detail":"Daily spend rose from 0.90 to 4.20 EUR (+367%)."}]}]}"""),
    };

    private static string Reply(string summary, string impact) =>
        System.Text.Json.JsonSerializer.Serialize(new { summary, findings = new[] { new { id = "F-COST", impact, citation = "r2" } } });

    [Fact]
    public void Parse_WithFiguresFromResults_AcceptsNumbersTheResultsHold_WrittenAnyWay()
    {
        var narration = NarrationParser.Parse(Reply("Total changed +80.5% over 7 days.", "log-1 rose from 0.9 to 4.2 EUR (+367%)."), CostResults, figuresFromResults: true);

        narration.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Total changed +81% over 7 days.", "ok")]
    [InlineData("Total changed +80.5%.", "log-1 now costs 5.00 EUR a day.")]
    public void Parse_WithFiguresFromResults_RefusesANumberNoResultHolds(string summary, string impact)
    {
        var parse = () => NarrationParser.Parse(Reply(summary, impact), CostResults, figuresFromResults: true);

        parse.Should().Throw<AgentRunException>().WithMessage("*not in any result*");
    }

    [Fact]
    public void Parse_WithoutFiguresFromResults_DoesNotCheckNumbers()
    {
        NarrationParser.Parse(Reply("Anything 12345.", "Anything 999."), CostResults).Items.Should().ContainSingle();
    }
}
