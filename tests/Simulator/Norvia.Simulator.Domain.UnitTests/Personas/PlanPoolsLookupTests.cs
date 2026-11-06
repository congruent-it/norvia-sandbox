using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.UnitTests.Plans;

namespace Norvia.Simulator.Domain.UnitTests.Personas;

public sealed class PlanPoolsLookupTests
{
    private static readonly TextPool Declines = new("german", "de", TextCategory.DeclineReason, "built-in", ["Leider nicht"]);

    private static SimulationPlan Plan() =>
        BookingPlanGenerator.Generate(TestWorld.Settings(5, 0, 1), TestWorld.Benelux, 1) with
        {
            Pools = [Declines],
            CarrierProfiles = new Dictionary<string, string> { ["CAR-0002"] = "german" },
        };

    [Fact]
    public void PoolFor_ACarrierWithAProfile_FindsThePoolOfThatKind()
    {
        Plan().PoolFor("CAR-0002", TextCategory.DeclineReason).Should().BeSameAs(Declines);
    }

    [Fact]
    public void PoolFor_AnotherKindAnotherCarrierOrAPlanFromBeforeThePools_FindsNothing()
    {
        Plan().PoolFor("CAR-0002", TextCategory.DeliveryFailedRemark).Should().BeNull();
        Plan().PoolFor("CAR-0001", TextCategory.DeclineReason).Should().BeNull();
        BookingPlanGenerator.Generate(TestWorld.Settings(5, 0, 1), TestWorld.Benelux, 1).PoolFor("CAR-0002", TextCategory.DeclineReason).Should().BeNull();
    }
}
