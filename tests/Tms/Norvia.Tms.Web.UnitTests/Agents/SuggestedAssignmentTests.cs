using Norvia.Agents;
using Norvia.Tms.Web.Agents;

namespace Norvia.Tms.Web.UnitTests.Agents;

public sealed class SuggestedAssignmentTests
{
    private static readonly AgentRun Run = new("planner", "run-7");

    private static Suggestion Of(SuggestedActionKind action, AgentRun? run = null, string? carrierId = null) =>
        new("cause", [new Evidence("claim", "r1", "get_assignment_options")], "Assign it.", Confidence.High, action, run, carrierId);

    [Fact]
    public void From_OwnFleet_PreselectsOwnFleet_WithNoCarrier()
    {
        var suggested = SuggestedAssignment.From("TO-0000042", Of(SuggestedActionKind.AssignOwnFleet, Run));

        suggested.Should().Be(new SuggestedAssignment("TO-0000042", null, Run));
    }

    [Fact]
    public void From_ACarrier_PreselectsThatCarrier()
    {
        var suggested = SuggestedAssignment.From("TO-0000042", Of(SuggestedActionKind.AssignCarrier, Run, "CAR-0002"));

        suggested.Should().Be(new SuggestedAssignment("TO-0000042", "CAR-0002", Run));
    }

    [Theory]
    [InlineData(SuggestedActionKind.NeedsAPerson)]
    [InlineData(SuggestedActionKind.Wait)]
    [InlineData(SuggestedActionKind.Release)]
    public void From_ASuggestionThatSuggestsNoAssignment_PreselectsNothing(SuggestedActionKind action) =>
        SuggestedAssignment.From("TO-0000042", Of(action, Run, "CAR-0002")).Should().BeNull();

    [Fact]
    public void From_ASuggestionWithNoRunToName_PreselectsNothing() =>
        SuggestedAssignment.From("TO-0000042", Of(SuggestedActionKind.AssignOwnFleet)).Should().BeNull();

    [Fact]
    public void From_ACarrierSuggestionNamingNoCarrier_PreselectsNothing()
    {
        SuggestedAssignment.From("TO-0000042", Of(SuggestedActionKind.AssignCarrier, Run)).Should().BeNull();
        SuggestedAssignment.From("TO-0000042", Of(SuggestedActionKind.AssignCarrier, Run, string.Empty)).Should().BeNull();
    }

    [Fact]
    public void RunFor_TheOrderAssignedAsSuggested_NamesTheRun() =>
        new SuggestedAssignment("TO-0000042", "CAR-0002", Run).RunFor("TO-0000042", "CAR-0002").Should().Be(Run);

    [Theory]
    [InlineData("TO-0000043", "CAR-0002")]
    [InlineData("TO-0000042", "CAR-0003")]
    [InlineData("TO-0000042", null)]
    public void RunFor_AnotherOrderOrAnotherAssignee_NamesNoRun(string number, string? carrierId) =>
        new SuggestedAssignment("TO-0000042", "CAR-0002", Run).RunFor(number, carrierId).Should().BeNull();

    [Fact]
    public void RunFor_OwnFleetSuggestedAndChosen_NamesTheRun() =>
        new SuggestedAssignment("TO-0000042", null, Run).RunFor("TO-0000042", null).Should().Be(Run);
}
