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
}
