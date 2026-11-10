using Norvia.Agents;
using Norvia.Tms.Api.Agents;

namespace Norvia.Tms.Api.UnitTests;

public sealed class ActingOnCallsTests
{
    [Fact]
    public void Check_NeitherOrBoth_IsValid()
    {
        var errors = new Dictionary<string, string[]>();

        ActingOnCalls.Check(errors, "AgentRunId", null, null);
        ActingOnCalls.Check(errors, "AgentRunId", "planner", "run-7");

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("planner", null)]
    [InlineData(null, "run-7")]
    [InlineData(" ", "run-7")]
    public void Check_OneWithoutTheOther_IsRefused(string? name, string? run)
    {
        var errors = new Dictionary<string, string[]>();

        ActingOnCalls.Check(errors, "AgentRunId", name, run);

        errors.Should().ContainKey("AgentRunId");
    }

    [Fact]
    public void Check_TooLong_IsRefused()
    {
        var errors = new Dictionary<string, string[]>();

        ActingOnCalls.Check(errors, "AgentRunId", new string('a', 101), "run-7");
        ActingOnCalls.Check(errors, "Other", "planner", new string('r', 201));

        errors.Keys.Should().BeEquivalentTo("AgentRunId", "Other");
    }

    [Fact]
    public void Of_ANameAndRun_IsTheTrimmedRun_AndNothingGivenIsNull()
    {
        ActingOnCalls.Of(" planner ", " run-7 ").Should().Be(new AgentRun("planner", "run-7"));
        ActingOnCalls.Of(null, null).Should().BeNull();
    }
}
