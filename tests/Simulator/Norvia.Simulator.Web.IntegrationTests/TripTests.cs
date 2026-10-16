extern alias SimulatorWeb;

using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The Simulator's Trips and Drivers at its boundary: the Planner's Trips for the Own fleet through the TMS API, and the Drivers' work on the Trips of today
/// through the Driver app API, with the Logic Apps' stand-ins between them and the TMS. The TMS gives a Vehicle and a Driver one Trip a day, and what a test
/// puts on a day is still there for the next, so each test runs the Own fleet of a Vehicle and a Driver of its own.
/// </summary>
[Collection("Simulator runs")]
public sealed class TripTests(SimulatorFixture fixture)
{
    // The Shipper whose Lanes the Own fleet's Vehicles carry the loads of.
    private const string Shipper = "SHP-0001";

    private static readonly DriverShares NothingWrong = new(CollectionFailedPercent: 0, DeliveryFailedPercent: 0, DamagedPercent: 0, ReservationsPercent: 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnFleetShipments_AreBuiltIntoDispatchedTrips_WithinTheRules_OrTheirPlacingIsLogged()
    {
        await using var simulator = await OwnFleetAsync("VEH-0003", "DRV-0003");
        var settings = Runs.Tripped(orders: 8, Runs.NextSeed(), collectedTodayPercent: 0, NothingWrong);

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, Runs.TripPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var built = TripsBuilt(log);
        built.Should().NotBeEmpty("the Planner builds Trips for the Own fleet's Shipments");
        var released = log.Where(line => line.Outcome == LogOutcome.Released).Select(line => line.ShipmentNumber!).ToList();
        released.Should().NotBeEmpty("an order meant for the Own fleet goes there only where the TMS says it fits, so not every order is released");

        var onTrips = new List<string>();
        foreach (var line in built)
        {
            var dispatch = log.Single(candidate => candidate.Action == TripActions.DispatchTrip && candidate.TripNumber == line.TripNumber);
            dispatch.Outcome.Should().Be(LogOutcome.TripDispatched);
            dispatch.CorrelationId.Should().NotBeNullOrWhiteSpace("the Dispatch's correlation ID opens the Trip's trail");

            var detail = (await fixture.TripAsync(line.TripNumber!, Ct))!;
            var trip = detail["trip"]!;
            trip["status"]!.GetValue<string>().Should().Be("Dispatched", "the Trips of later days stay Dispatched");
            trip["vehicleId"]!.GetValue<string>().Should().Be("VEH-0003");
            trip["driverId"]!.GetValue<string>().Should().Be("DRV-0003");
            detail["failures"]!.AsArray().Should().BeEmpty("the Trip breaks none of the TMS's rules");
            TimeSpan.Parse(trip["plannedDrivingDuration"]!.GetValue<string>(), CultureInfo.InvariantCulture)
                .Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromHours(8));
            trip["loadWeightKg"]!.GetValue<decimal>().Should().BeLessThanOrEqualTo(trip["payloadKg"]!.GetValue<int>());
            trip["loadPalletPlaces"]!.GetValue<int>().Should().BeLessThanOrEqualTo(trip["vehiclePalletPlaces"]!.GetValue<int>());
            var day = DateOnly.Parse(trip["day"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            TimeOnly.Parse(trip["plannedStart"]!.GetValue<string>(), CultureInfo.InvariantCulture)
                .Should().Be(await fixture.ShiftStartAsync("DRV-0003", day.DayOfWeek, Ct), "the planned start is the start of the Driver's Shift");
            var stops = detail["stops"]!.AsArray();
            trip["stopCount"]!.GetValue<int>().Should().Be(stops.Count);
            var shipments = stops.Select(stop => stop!["shipmentNumber"]!.GetValue<string>()).Distinct().ToList();
            stops.Count.Should().Be(2 * shipments.Count, "each Shipment is a collection and a delivery");
            line.Detail.Should().Contain($", {shipments.Count} {(shipments.Count == 1 ? "Shipment" : "Shipments")},", "the Create Trip line names how many Shipments the Trip carries");
            shipments.Should().NotIntersectWith(onTrips, "a Shipment is on one Trip only");
            onTrips.AddRange(shipments);
        }

        await Runs.WaitUntilAsync(async () => await AllHaveStatusAsync(onTrips, "Dispatched"), Ct);
        var unplaced = log.Count(line => line.Outcome == LogOutcome.Info && line.Action.Contains("found no Vehicle and Driver left", StringComparison.Ordinal));
        onTrips.Count.Should().Be(released.Count - unplaced, "each released Shipment is on a Trip, or its lack of room is logged");
        onTrips.Should().BeSubsetOf(released);
        log.Any(Runs.IsDriver).Should().BeFalse("no Trip is for today, so no Driver starts one");
    }

    [Fact]
    public async Task TheTripsOfToday_AreWorkedInOrderToCompleted_WithReservationsInExactlyTheConfiguredShare_AndTheTripsOfLaterDaysStayDispatched()
    {
        Assert.SkipWhen(!WorksToday(), "No Driver has a Shift at the weekend, so no Trip can be for today.");
        await using var simulator = await OwnFleetAsync("VEH-0004", "DRV-0004");
        var settings = Runs.Tripped(orders: 8, Runs.NextSeed(), collectedTodayPercent: 50, new DriverShares(0, 0, 0, ReservationsPercent: 50));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, Runs.TripPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var today = Today();
        var built = TripsBuilt(log);
        var ofToday = built.Where(line => line.Detail!.StartsWith(today, StringComparison.Ordinal)).ToList();
        var later = built.Except(ofToday).ToList();
        ofToday.Should().NotBeEmpty("half of the orders are collected today");
        later.Should().NotBeEmpty("and half on later days");

        foreach (var line in ofToday)
        {
            var detail = (await fixture.TripAsync(line.TripNumber!, Ct))!;
            detail["trip"]!["status"]!.GetValue<string>().Should().Be("Completed");
            var stops = detail["stops"]!.AsArray();
            detail["trip"]!["stopsDone"]!.GetValue<int>().Should().Be(stops.Count, "the Driver reported every Stop");
            var shipments = stops.Select(stop => stop!["shipmentNumber"]!.GetValue<string>()).Distinct().ToList();

            var trail = (await simulator.Requests.ListLogAsync(new SimulationRunId(run.Id), new LogFilter(Text: line.TripNumber), Ct)).OrderBy(entry => entry.Sequence).ToList();
            trail.Select(entry => entry.Action).Should().Equal(
                [TripActions.CreateTrip, TripActions.DispatchTrip, DriverActions.StartTrip,
                    .. shipments.SelectMany(_ => new[] { DriverActions.Collected, DriverActions.Deliver }), DriverActions.EndTrip, DriverActions.EndTrip],
                "the Driver works the Trip in order: start, each Shipment collected then delivered, end");
            trail.Skip(2).Take(trail.Count - 3).Should().OnlyContain(entry => entry.Outcome == LogOutcome.DriverReported);
            trail[^1].Outcome.Should().Be(LogOutcome.TripCompleted);
            // A Draft is the Planner's own work and publishes nothing, so Create Trip has no trail; every call after it does.
            trail.Should().OnlyContain(entry => entry.TripNumber == line.TripNumber);
            trail.Skip(1).Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.CorrelationId));
            trail.Skip(2).Select(entry => entry.Actor).Distinct().Should().Equal(["Fatima El Amrani"], "each Driver acts as themselves");

            await Runs.WaitUntilAsync(async () => await AllDeliveredAsync(shipments), Ct);
        }

        var delivered = log.Where(line => line.Action == DriverActions.Deliver).ToList();
        var expectedReservations = Share(delivered.Count, 50);
        expectedReservations.Should().BeGreaterThan(0);
        delivered.Count(line => line.Detail!.Contains("With reservations", StringComparison.Ordinal)).Should().Be(expectedReservations);
        log.Count(line => line.Outcome == LogOutcome.ExceptionFound).Should().Be(expectedReservations, "each delivery with reservations opened an Exception");

        foreach (var line in later)
        {
            (await fixture.TripAsync(line.TripNumber!, Ct))!["trip"]!["status"]!.GetValue<string>().Should().Be("Dispatched");
            log.Should().NotContain(entry => Runs.IsDriver(entry) && entry.TripNumber == line.TripNumber, "a Driver starts a Trip on its day");
        }
    }

    [Fact]
    public async Task TheFailedStops_AreExactlyTheConfiguredShares_OpenExceptions_AndTheFailedDeliveriesGoOnARedeliveryTrip()
    {
        Assert.SkipWhen(!WorksToday(), "No Driver has a Shift at the weekend, so no Trip can be for today.");
        await using var simulator = await OwnFleetAsync("VEH-0005", "DRV-0005");
        var shares = new DriverShares(CollectionFailedPercent: 20, DeliveryFailedPercent: 40, DamagedPercent: 10, ReservationsPercent: 0);
        var settings = Runs.Tripped(orders: 10, seed: Runs.NextSeed(), collectedTodayPercent: 100, shares);

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, Runs.TripPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var driver = log.Where(Runs.IsDriver).ToList();
        var ran = driver.Count(line => line.Action is DriverActions.Collected or DriverActions.CollectionFailed && line.Outcome == LogOutcome.DriverReported);
        ran.Should().BeGreaterThan(2, "the Own fleet's Vehicle carries some of the day's Shipments");
        // Plain arithmetic, not the Simulator's own funnel: each share is of all the Shipments that ran, and a Shipment fails at most once, so deliveries fail only among those collected.
        var collectionsFailed = Share(ran, 20);
        var deliveriesFailed = Math.Min(Share(ran, 40), ran - collectionsFailed);
        var damaged = Share(ran, 10);
        collectionsFailed.Should().BeGreaterThan(0);
        deliveriesFailed.Should().BeGreaterThan(0);

        driver.Count(line => line.Action == DriverActions.CollectionFailed).Should().Be(collectionsFailed);
        driver.Count(line => line.Action == DriverActions.DeliveryFailed).Should().Be(deliveriesFailed);
        driver.Count(line => line.Action == DriverActions.ReportDamage).Should().Be(damaged);
        driver.Count(line => line.Action == DriverActions.Deliver).Should().Be(ran - collectionsFailed - deliveriesFailed);

        var found = log.Where(line => line.Outcome == LogOutcome.ExceptionFound).ToList();
        found.Should().HaveCount(collectionsFailed + deliveriesFailed + damaged, "every failure and every damage report opened an Exception");
        log.Should().NotContain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("didn't open in time", StringComparison.Ordinal));
        var open = (await fixture.OpenExceptionsAsync(Ct)).Select(exception => exception!["shipmentNumber"]!.GetValue<string>()).ToHashSet();
        open.Should().Contain(found.Select(line => line.ShipmentNumber!));

        var collectionFailed = driver.Where(line => line.Action == DriverActions.CollectionFailed).Select(line => line.ShipmentNumber!).ToList();
        await Runs.WaitUntilAsync(async () => await AllHaveStatusAsync(collectionFailed, "Planned"), Ct);

        var deliveryFailed = driver.Where(line => line.Action == DriverActions.DeliveryFailed).Select(line => line.ShipmentNumber!).ToList();
        var noRoom = log.Count(line => line.Outcome == LogOutcome.Info && line.Action.Contains("for its Re-delivery", StringComparison.Ordinal));
        var redelivery = TripsBuilt(log).Where(line => string.CompareOrdinal(line.Detail![..10], Today()) > 0).ToList();
        redelivery.Should().NotBeEmpty("the Shipments whose delivery failed wait for a Re-delivery on a Trip of a later day");
        var redelivered = new List<string>();
        foreach (var line in redelivery)
        {
            var detail = (await fixture.TripAsync(line.TripNumber!, Ct))!;
            detail["trip"]!["status"]!.GetValue<string>().Should().Be("Dispatched");
            redelivered.AddRange(detail["stops"]!.AsArray().Select(stop => stop!["shipmentNumber"]!.GetValue<string>()));
        }

        redelivered.Distinct().Should().BeSubsetOf(deliveryFailed).And.HaveCount(deliveryFailed.Count - noRoom);
    }

    [Fact]
    public async Task ATripTheTmsRefuses_IsLoggedWithItsCode_AndTheShipmentsStayReleased_AndTheRunGoesOn()
    {
        // The Simulator believes this Driver holds the licence for the Vehicle that the TMS knows they do not.
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(
            Ct, SeedEdits.OnlyShippers(Shipper), SeedEdits.OwnFleetOf("VEH-0006", "DRV-0006", "B", "C", "CE"));
        var settings = Runs.Tripped(orders: 4, Runs.NextSeed(), collectedTodayPercent: 0, NothingWrong);

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, Runs.TripPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var refused = log.Where(line => line.Outcome == LogOutcome.PlanningRefused && line.Action == TripActions.CreateTrip).ToList();
        refused.Should().NotBeEmpty();
        refused.Should().OnlyContain(line => line.Detail != null
            && line.Detail.Contains("LicenceNotHeld", StringComparison.Ordinal) && line.Detail.EndsWith("(409)", StringComparison.Ordinal));
        log.Should().NotContain(line => line.Outcome == LogOutcome.TripBuilt || line.Outcome == LogOutcome.TripDispatched);
        log.Any(Runs.IsDriver).Should().BeFalse();
        foreach (var number in log.Where(line => line.Outcome == LogOutcome.Released).Select(line => line.ShipmentNumber!))
        {
            (await fixture.ShipmentAsync(number, Ct))!["status"]!.GetValue<string>().Should().Be("Released");
        }
    }

    private static List<LogLine> TripsBuilt(IReadOnlyList<LogLine> log) =>
        [.. log.Where(line => line.Outcome == LogOutcome.TripBuilt && line.Action == TripActions.CreateTrip).OrderBy(line => line.Sequence)];

    // A share of a number of Shipments, rounded as the Simulator does: halves away from zero.
    private static int Share(int of, int percent) => (int)Math.Round(of * percent / 100.0, MidpointRounding.AwayFromZero);

    private static string Today() => PickupSlot.Today(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The Drivers' rosters are Monday to Friday, so there is no Shift, and so no Trip, today at the weekend.
    private static bool WorksToday() => PickupSlot.Today(DateTimeOffset.UtcNow).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    private async Task<SimulatorHost> OwnFleetAsync(string vehicleId, string driverId) =>
        await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(Shipper), SeedEdits.OwnFleetOf(vehicleId, driverId));

    private async Task<bool> AllDeliveredAsync(IEnumerable<string> shipmentNumbers) => await AllHaveStatusAsync(shipmentNumbers, "Delivered");

    private async Task<bool> AllHaveStatusAsync(IEnumerable<string> shipmentNumbers, string status)
    {
        foreach (var number in shipmentNumbers)
        {
            if (await fixture.ShipmentAsync(number, Ct) is not JsonObject shipment || shipment["status"]!.GetValue<string>() != status)
            {
                return false;
            }
        }

        return true;
    }
}
