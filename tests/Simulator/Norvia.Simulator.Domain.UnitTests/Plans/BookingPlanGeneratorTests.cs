using System.Text.Json;
using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Domain.UnitTests.Plans;

public sealed class BookingPlanGeneratorTests
{
    private static SimulationPlan Plan(int orders = 40, int badShare = 25, int seed = 7, int run = 3) =>
        BookingPlanGenerator.Generate(TestWorld.Settings(orders, badShare, seed), TestWorld.Benelux, run);

    [Fact]
    public void Generate_TheSameSeed_GivesTheSamePlan()
    {
        var first = JsonSerializer.Serialize(Plan(seed: 42));
        var second = JsonSerializer.Serialize(Plan(seed: 42));

        second.Should().Be(first);
    }

    [Fact]
    public void Generate_AnotherSeed_GivesAnotherPlan()
    {
        JsonSerializer.Serialize(Plan(seed: 1)).Should().NotBe(JsonSerializer.Serialize(Plan(seed: 2)));
    }

    [Fact]
    public void Generate_NOrders_GivesNBookingsNumberedFromOne()
    {
        var plan = Plan(orders: 25);

        plan.Bookings.Select(booking => booking.Step).Should().Equal(Enumerable.Range(1, 25));
    }

    [Fact]
    public void Generate_ABadShare_MakesExactlyThatShareOfTheBookingsBad()
    {
        var plan = Plan(orders: 40, badShare: 25);

        plan.Bookings.Count(booking => booking.Bad is not null).Should().Be(10);
    }

    [Fact]
    public void Generate_NoBadShare_MakesNoBookingBad()
    {
        Plan(badShare: 0).Bookings.Should().OnlyContain(booking => booking.Bad == null);
    }

    [Fact]
    public void Generate_GoodBookings_HaveReferencesOfTheRunAndTheirStep()
    {
        var plan = Plan(badShare: 0, run: 12);

        plan.Bookings.Should().OnlyContain(booking => booking.Reference == $"SIM-12-{booking.Step}");
    }

    [Fact]
    public void Generate_GoodBookings_FollowALaneOfTheirShippersRateCard()
    {
        var plan = Plan(orders: 60, badShare: 0);

        foreach (var booking in plan.Bookings)
        {
            var shipper = TestWorld.Benelux.Shippers.Single(candidate => candidate.Id == booking.ShipperId);
            shipper.Lanes.Should().Contain(lane => lane.Origin == booking.PickupZone && lane.Destination == booking.DeliveryZone);
        }
    }

    [Fact]
    public void Generate_Addresses_LieInTheZonesOfTheLane()
    {
        var plan = Plan(orders: 60, badShare: 0);

        foreach (var booking in plan.Bookings)
        {
            var shipper = TestWorld.Benelux.Shippers.Single(candidate => candidate.Id == booking.ShipperId);
            var zone = shipper.Zones.Single(candidate => candidate.Name == booking.DeliveryZone);
            booking.Delivery.Country.Should().Be(zone.Country);
            zone.PostcodePrefixes.Should().Contain(prefix => booking.Delivery.Postcode.Replace(" ", "", StringComparison.Ordinal).StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Generate_Bookings_ArePlacedAsTheirShippersPersona()
    {
        var plan = Plan(orders: 30);

        plan.Bookings.Should().OnlyContain(booking =>
            (booking.ShipperId == "SHP-0001" && booking.PersonaId == "brouwer-dranken")
            || (booking.ShipperId == "SHP-0003" && booking.PersonaId == "tuincentrum-groenrijk"));
    }

    [Fact]
    public void Generate_Goods_StayWithinWhatARateCardCanPrice()
    {
        var plan = Plan(orders: 80, badShare: 0);

        plan.Bookings.Should().OnlyContain(booking => booking.Goods.Count >= 1 && booking.TotalWeightKg > 0 && booking.TotalWeightKg <= 24000);
    }

    [Fact]
    public void Generate_ASharePickedUpToday_HasThatManyBookingsWithADayOffsetOfZero()
    {
        var plan = BookingPlanGenerator.Generate(TestWorld.Settings(orders: 100, badShare: 0, today: 30), TestWorld.Benelux, 1);

        plan.Bookings.Count(booking => booking.Slot.DayOffset == 0).Should().Be(30);
        plan.Bookings.Should().OnlyContain(booking => booking.Slot.DayOffset >= 0 && booking.Slot.DayOffset <= BookingPlanGenerator.PlanningHorizonDays);
    }

    [Fact]
    public void Generate_ConflictingReference_ReusesAnEarlierGoodBookingsReferenceForOtherGoods()
    {
        var plan = Plan(orders: 120, badShare: 60, seed: 5);

        var conflicts = plan.Bookings.Where(booking => booking.Bad == BadBookingKind.ConflictingReference).ToList();
        conflicts.Should().NotBeEmpty();
        foreach (var conflict in conflicts)
        {
            var original = plan.Bookings.Single(booking => booking.Reference == conflict.Reference && booking.Bad is null);
            original.Step.Should().BeLessThan(conflict.Step);
            conflict.ShipperId.Should().Be(original.ShipperId);
            conflict.TotalWeightKg.Should().NotBe(original.TotalWeightKg);
        }
    }

    [Fact]
    public void Generate_RepeatedReference_SendsAnEarlierGoodBookingAgainUnchanged()
    {
        var plan = Plan(orders: 120, badShare: 60, seed: 5);

        var repeats = plan.Bookings.Where(booking => booking.Bad == BadBookingKind.RepeatedReference).ToList();
        repeats.Should().NotBeEmpty();
        foreach (var repeat in repeats)
        {
            var original = plan.Bookings.Single(booking => booking.Reference == repeat.Reference && booking.Bad is null);
            original.Step.Should().BeLessThan(repeat.Step);
            repeat.Should().BeEquivalentTo(original with { Step = repeat.Step, Bad = BadBookingKind.RepeatedReference });
        }
    }

    [Fact]
    public void Generate_NoLane_DeliversOutsideEveryZoneOfTheRateCard()
    {
        var plan = Plan(orders: 120, badShare: 60, seed: 5);

        var noLane = plan.Bookings.Where(booking => booking.Bad == BadBookingKind.NoLane).ToList();
        noLane.Should().NotBeEmpty();
        noLane.Should().OnlyContain(booking => booking.Delivery.Country == "DE");
    }

    [Fact]
    public void Generate_InvalidContent_LeavesOutWhatNorviaNeeds()
    {
        var plan = Plan(orders: 120, badShare: 60, seed: 5);

        var invalid = plan.Bookings.Where(booking => booking.Bad == BadBookingKind.InvalidContent).ToList();
        invalid.Should().NotBeEmpty();
        invalid.Should().OnlyContain(booking =>
            string.IsNullOrWhiteSpace(booking.ConsigneeName) || booking.Goods.Count == 0 || booking.Goods.Any(line => line.WeightKg <= 0));
    }

    [Fact]
    public void Generate_ABadBookingNeedingAnEarlierOne_FallsBackToInvalidContentWhenThereIsNone()
    {
        var plan = BookingPlanGenerator.Generate(TestWorld.Settings(orders: 1, badShare: 100), TestWorld.Benelux, 1);

        plan.Bookings.Single().Bad.Should().NotBe(BadBookingKind.RepeatedReference).And.NotBe(BadBookingKind.ConflictingReference);
    }

    [Fact]
    public void Generate_AShipperWithoutLanes_IsLeftOut()
    {
        var world = new Worlds.SimulationWorld([TestWorld.Benelux.Shippers[0], TestWorld.Benelux.Shippers[1] with { Lanes = [] }]);

        var plan = BookingPlanGenerator.Generate(TestWorld.Settings(orders: 20, badShare: 0), world, 1);

        plan.Bookings.Should().OnlyContain(booking => booking.ShipperId == "SHP-0001");
    }

    [Fact]
    public void Generate_NoShipperToBookFor_Throws()
    {
        var act = () => BookingPlanGenerator.Generate(TestWorld.Settings(), new Worlds.SimulationWorld([]), 1);

        act.Should().Throw<InvalidOperationException>();
    }
}
