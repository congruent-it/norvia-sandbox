using System.Text.Json;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.UnitTests.Plans;

namespace Norvia.Simulator.Domain.UnitTests.Planner;

public sealed class PlannerChoicesTests
{
    private static SimulationPlan Plan(int orders = 40, int badShare = 25, int seed = 7, int assigned = 80, int ownFleet = 50, int reassign = 10) =>
        BookingPlanGenerator.Generate(TestWorld.Settings(orders, badShare, seed, assigned: assigned, ownFleet: ownFleet, reassign: reassign), TestWorld.Benelux, 3);

    [Fact]
    public void For_TheSameSeed_GivesTheSameChoices()
    {
        JsonSerializer.Serialize(Plan(seed: 42).Choices).Should().Be(JsonSerializer.Serialize(Plan(seed: 42).Choices));
    }

    [Fact]
    public void For_AnotherSeed_GivesOtherChoices()
    {
        JsonSerializer.Serialize(Plan(seed: 1).Choices).Should().NotBe(JsonSerializer.Serialize(Plan(seed: 2).Choices));
    }

    [Fact]
    public void For_ThePlannersChoices_LeaveTheBookingsAsTheyWere()
    {
        var withPlanner = Plan(assigned: 80);
        var withoutPlanner = Plan(assigned: 0);

        JsonSerializer.Serialize(withPlanner.Bookings).Should().Be(JsonSerializer.Serialize(withoutPlanner.Bookings));
    }

    [Fact]
    public void For_AnAssignedShare_AssignsExactlyThatShareOfTheOrdersNorviaWillAccept()
    {
        var plan = Plan(orders: 40, badShare: 25, assigned: 50);

        var accepted = plan.Bookings.Where(booking => booking.Expected == ExpectedAnswer.Accepted).Select(booking => booking.Step).ToHashSet();
        plan.Choices.Should().HaveCount((int)Math.Round(accepted.Count * 0.5, MidpointRounding.AwayFromZero));
        plan.Choices.Select(choice => choice.Step).Should().OnlyContain(step => accepted.Contains(step));
    }

    [Fact]
    public void For_BookingsNorviaWillNotAccept_AreNeverAssigned()
    {
        var plan = Plan(orders: 40, badShare: 100, assigned: 100);

        var expectedAccepted = plan.Bookings.Where(booking => booking.Expected == ExpectedAnswer.Accepted).Select(booking => booking.Step);
        plan.Choices.Select(choice => choice.Step).Should().BeEquivalentTo(expectedAccepted);
        plan.Bookings.Where(booking => booking.Expected != ExpectedAnswer.Accepted)
            .Should().NotBeEmpty().And.OnlyContain(booking => plan.Choices.All(choice => choice.Step != booking.Step));
    }

    [Fact]
    public void For_AnOwnFleetShare_MeansToTryOwnFleetForExactlyThatShareOfTheAssigned()
    {
        var plan = Plan(orders: 40, badShare: 0, assigned: 100, ownFleet: 25);

        plan.Choices.Count(choice => choice.Intent == PlannerIntent.OwnFleetIfItFits).Should().Be(10);
        plan.Choices.Count(choice => choice.Intent == PlannerIntent.Carrier).Should().Be(30);
    }

    [Fact]
    public void For_AReassignShare_ReassignsExactlyThatShareOfTheAssigned()
    {
        var plan = Plan(orders: 40, badShare: 0, assigned: 50, reassign: 20);

        plan.Choices.Count(choice => choice.Reassign).Should().Be(4);
    }

    [Fact]
    public void For_NothingToAssign_GivesNoChoices()
    {
        Plan(assigned: 0).Choices.Should().BeEmpty();
    }

    [Fact]
    public void Choices_SurviveAJsonRoundTripOfThePlan_AndAnOldPlanHasNone()
    {
        var plan = Plan();

        JsonSerializer.Deserialize<SimulationPlan>(JsonSerializer.Serialize(plan))!.Choices.Should().BeEquivalentTo(plan.Choices);
        JsonSerializer.Deserialize<SimulationPlan>("{\"RunNumber\":1,\"Settings\":" + JsonSerializer.Serialize(plan.Settings) + ",\"Bookings\":[]}")!.Choices.Should().BeEmpty();
    }
}
