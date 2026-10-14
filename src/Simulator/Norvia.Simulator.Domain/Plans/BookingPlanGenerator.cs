using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Plans;

/// <summary>
/// Makes a Simulation plan from the settings and the seed alone: the same ones always give the same plan. Shippers and
/// their Rate Cards come from the world (the seed file); Consignees, addresses and goods are generated, and a Lane is
/// chosen from the Shipper's Rate Card.
/// </summary>
public static class BookingPlanGenerator
{
    public const int PlanningHorizonDays = 7;

    private const int MaxLinesPerBooking = 3;

    public static SimulationPlan Generate(SimulationSettings settings, SimulationWorld world, int runNumber)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(world);

        var shippers = world.Shippers.Where(shipper => shipper.Lanes.Count > 0).ToList();
        if (shippers.Count == 0)
        {
            throw new InvalidOperationException("No Shipper has a Rate Card with a Lane to book for.");
        }

        var random = new SeededRandom(settings.Seed);
        var count = settings.OrderCount;
        var badSteps = StepsOf(random, count, settings.BadSharePercent);
        var todaySteps = StepsOf(random, count, settings.CollectedTodayPercent);

        var bookings = new List<PlannedBooking>(count);
        for (var step = 1; step <= count; step++)
        {
            var slot = todaySteps.Contains(step)
                ? new PickupSlot(0, random.Between(8, 15), random.Between(2, 4))
                : new PickupSlot(random.Between(1, PlanningHorizonDays), random.Between(8, 15), random.Between(2, 4));

            bookings.Add(badSteps.Contains(step)
                ? Bad(step, runNumber, slot, shippers, bookings, random)
                : Good(step, runNumber, slot, random.Pick(shippers), random));
        }

        return new SimulationPlan(runNumber, settings, bookings) { Choices = PlannerChoices.For(settings, bookings) };
    }

    // Exactly the share asked for, at positions the seed picks.
    private static HashSet<int> StepsOf(SeededRandom random, int count, int percent)
    {
        var wanted = (int)Math.Round(count * percent / 100.0, MidpointRounding.AwayFromZero);
        return [.. random.Shuffled(count).Take(wanted).Select(index => index + 1)];
    }

    private static PlannedBooking Good(int step, int runNumber, PickupSlot slot, SimulatedShipper shipper, SeededRandom random)
    {
        var lane = random.Pick(shipper.Lanes);
        var pickup = AddressPool.InZone(ZoneOf(shipper, lane.Origin), random);
        var delivery = AddressPool.InZone(ZoneOf(shipper, lane.Destination), random);
        return Booking(step, runNumber, slot, shipper, lane.Origin, lane.Destination, pickup, delivery, random);
    }

    private static PlannedBooking Bad(
        int step, int runNumber, PickupSlot slot, List<SimulatedShipper> shippers, List<PlannedBooking> earlier, SeededRandom random)
    {
        var goodEarlier = earlier.Where(booking => booking.Bad is null).ToList();
        var kinds = Enum.GetValues<BadBookingKind>().Where(kind => goodEarlier.Count > 0 || kind is BadBookingKind.NoLane or BadBookingKind.InvalidContent).ToList();
        var kind = random.Pick(kinds);

        if (kind == BadBookingKind.RepeatedReference)
        {
            return random.Pick(goodEarlier) with { Step = step, Bad = kind };
        }

        if (kind == BadBookingKind.ConflictingReference)
        {
            var original = random.Pick(goodEarlier);
            var first = original.Goods[0];
            var heavier = first with { WeightKg = first.WeightKg + random.Between(10, 90) };
            return original with { Step = step, Bad = kind, Goods = [heavier, .. original.Goods.Skip(1)] };
        }

        var shipper = random.Pick(shippers);
        var good = Good(step, runNumber, slot, shipper, random);
        return kind == BadBookingKind.NoLane
            ? good with { Bad = kind, DeliveryZone = "Germany", Delivery = AddressPool.Germany(random) }
            : Invalid(good, random);
    }

    private static PlannedBooking Invalid(PlannedBooking booking, SeededRandom random) => random.Next(3) switch
    {
        0 => booking with { Bad = BadBookingKind.InvalidContent, ConsigneeName = null },
        1 => booking with { Bad = BadBookingKind.InvalidContent, Goods = [] },
        _ => booking with { Bad = BadBookingKind.InvalidContent, Goods = [.. booking.Goods.Select(line => line with { WeightKg = 0 })] },
    };

    private static PlannedBooking Booking(
        int step, int runNumber, PickupSlot slot, SimulatedShipper shipper, string pickupZone, string deliveryZone,
        PlannedAddress pickup, PlannedAddress delivery, SeededRandom random) =>
        new(
            step, shipper.PersonaId, shipper.PersonaName, shipper.Id, shipper.Name, $"SIM-{runNumber}-{step}", null,
            pickupZone, deliveryZone, pickup, delivery, slot,
            AddressPool.Consignee(delivery.City, random), AddressPool.Contact(random), AddressPool.Phone(delivery.Country, random),
            Goods(random), Remarks: null);

    private static List<PlannedGoodsLine> Goods(SeededRandom random)
    {
        var lines = new List<PlannedGoodsLine>();
        for (var i = random.Between(1, MaxLinesPerBooking); i > 0; i--)
        {
            var package = random.Pick(Enum.GetValues<PackageKind>());
            var quantity = random.Between(1, package == PackageKind.Pallet ? 8 : 20);
            var weight = quantity * random.Between(package == PackageKind.Pallet ? 150 : 5, package == PackageKind.Pallet ? 450 : 40);
            lines.Add(package == PackageKind.Loose
                ? new PlannedGoodsLine(package, quantity, weight, null, null, null)
                : new PlannedGoodsLine(package, quantity, weight, random.Between(40, 120), random.Between(30, 80), random.Between(20, 150)));
        }

        return lines;
    }

    private static RateZone ZoneOf(SimulatedShipper shipper, string name) =>
        shipper.Zones.FirstOrDefault(zone => zone.Name == name)
        ?? throw new InvalidOperationException($"Shipper {shipper.Id}'s Rate Card has a Lane to Zone '{name}', which it doesn't define.");
}
