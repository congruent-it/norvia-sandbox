using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

public sealed class PlanningActionTests
{
    [Theory]
    [InlineData(PlanningAction.Assignment, "assigned")]
    [InlineData(PlanningAction.Reassignment, "reassigned")]
    [InlineData(PlanningAction.Release, "released")]
    [InlineData(PlanningAction.Withdrawal, "withdrawn")]
    public void Done_NamesWhatTheActionDoesToAShipment(PlanningAction action, string done)
    {
        action.Done().Should().Be(done);
    }

    [Theory]
    [InlineData(PlanningAction.Assignment, "An assignment")]
    [InlineData(PlanningAction.Reassignment, "A reassignment")]
    [InlineData(PlanningAction.Release, "A release")]
    [InlineData(PlanningAction.Withdrawal, "A withdrawal")]
    public void AsSubject_OpensASentenceAboutTheAction(PlanningAction action, string subject)
    {
        action.AsSubject().Should().Be(subject);
    }
}
