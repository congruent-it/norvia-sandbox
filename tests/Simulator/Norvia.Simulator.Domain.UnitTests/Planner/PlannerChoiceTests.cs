using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.UnitTests.Planner;

public sealed class PlannerChoiceTests
{
    private static readonly SimulatedCarrier Kessler = new("CAR-0002", "Kessler Spedition", true);
    private static readonly SimulatedCarrier VanDijk = new("CAR-0001", "Van Dijk Transport", true);

    private static PlannerChoice Choice(int carrierPick = 0, int reassignPick = 0) =>
        new(1, PlannerIntent.Carrier, carrierPick, Reassign: true, reassignPick);

    [Theory]
    [InlineData(0, "CAR-0001")]
    [InlineData(1, "CAR-0002")]
    [InlineData(2, "CAR-0001")]
    [InlineData(1_000_001, "CAR-0002")]
    public void Carrier_IndexesTheActiveCarriersInOrder(int pick, string expected)
    {
        var carrier = Choice(carrierPick: pick).Carrier([VanDijk, Kessler]);

        carrier!.CarrierId.Should().Be(expected);
    }

    [Fact]
    public void Carrier_NoActiveCarrier_IsNull()
    {
        Choice().Carrier([]).Should().BeNull();
    }

    [Fact]
    public void ReassignTarget_FromOwnFleet_IsACarrier()
    {
        Choice(reassignPick: 1).ReassignTarget(Assignee.OwnFleet, [VanDijk, Kessler]).CarrierId.Should().Be("CAR-0002");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void ReassignTarget_FromACarrier_IsAnotherOne(int pick)
    {
        var target = Choice(reassignPick: pick).ReassignTarget(Assignee.Of(VanDijk), [VanDijk, Kessler]);

        target.CarrierId.Should().Be("CAR-0002");
    }

    [Fact]
    public void ReassignTarget_FromTheOnlyCarrier_IsOwnFleet()
    {
        Choice().ReassignTarget(Assignee.Of(VanDijk), [VanDijk]).Should().Be(Assignee.OwnFleet);
    }

    [Fact]
    public void ReassignTarget_NoCarrierAtAll_IsOwnFleet()
    {
        Choice().ReassignTarget(Assignee.OwnFleet, []).IsOwnFleet.Should().BeTrue();
    }
}
