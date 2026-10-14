using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.UnitTests.Plans;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.UnitTests.Planner;

public sealed class PlannerDecisionTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);
    private static readonly SimulatedCarrier VanDijk = new("CAR-0001", "Van Dijk Transport", true);

    private static readonly PlannedBooking Booking = BookingPlanGenerator
        .Generate(TestWorld.Settings(orders: 1, badShare: 0, assigned: 0), TestWorld.Benelux, 1).Bookings[0];

    private static PlannerChoice Choice(PlannerIntent intent) => new(1, intent, 0, Reassign: false, 0);

    private static FleetLedger Ledger(decimal capacityKg) => new([new FleetDay(Day, capacityKg, 1_000, 0, 0)]);

    [Fact]
    public void Decide_OwnFleetWithRoom_TakesTheRoom()
    {
        var ledger = Ledger(100_000);

        var decision = Choice(PlannerIntent.OwnFleetIfItFits).Decide(Booking, Day, [VanDijk], ledger);

        decision.Should().Be(new PlannerDecision(Assignee.OwnFleet, true, "Own fleet has room that day"));
    }

    [Fact]
    public void Decide_OwnFleetFull_GoesToACarrierAndTakesNothing()
    {
        var decision = Choice(PlannerIntent.OwnFleetIfItFits).Decide(Booking, Day, [VanDijk], Ledger(0));

        decision.Assignee.CarrierId.Should().Be("CAR-0001");
        decision.ReservedOwnFleet.Should().BeFalse();
        decision.Reason.Should().Be("Own fleet has no room that day: Van Dijk Transport");
    }

    [Fact]
    public void Decide_OwnFleetFullAndNoCarrier_StaysWithOwnFleet()
    {
        var decision = Choice(PlannerIntent.OwnFleetIfItFits).Decide(Booking, Day, [], Ledger(0));

        decision.Assignee.Should().Be(Assignee.OwnFleet);
        decision.ReservedOwnFleet.Should().BeFalse();
    }

    [Fact]
    public void Decide_ACarrierIntent_GoesToACarrierWithoutLookingAtOwnFleet()
    {
        var ledger = Ledger(100_000);

        var decision = Choice(PlannerIntent.Carrier).Decide(Booking, Day, [VanDijk], ledger);

        decision.Should().Be(new PlannerDecision(Assignee.Of(VanDijk), false, "To Van Dijk Transport"));
        ledger.TryReserve(Day, new FleetLoad(100_000, 0)).Should().BeTrue("nothing was taken");
    }

    [Fact]
    public void Decide_ACarrierIntentAndNoCarrier_StaysWithOwnFleet()
    {
        Choice(PlannerIntent.Carrier).Decide(Booking, Day, [], Ledger(0)).Assignee.Should().Be(Assignee.OwnFleet);
    }
}
