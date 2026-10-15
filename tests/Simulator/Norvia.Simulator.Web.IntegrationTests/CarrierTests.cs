extern alias SimulatorWeb;

using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

[Collection("Simulator runs")]
public sealed class CarrierTests(SimulatorFixture fixture)
{
    private static readonly string[] Journey = ["Collected", "Departed", "Arrived", "OutForDelivery", "Delivered"];
    private static readonly string[] Prefixes = ["VDT", "KES", "PKT"];

    // The Shipper whose every Lane the Carriers' Rate Cards cover, so the TMS can price each release; and the one whose Lanes none of them covers.
    private const string PricedShipper = "SHP-0001";
    private const string UnpricedShipper = "SHP-0003";

    // Every step asked for in full, so a test names only the steps it is about.
    private static readonly CarrierShares Everything = new(
        ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 0,
        DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BookedShipments_MoveToDelivered_WithTrackingInOrderAndAProofOfDelivery()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 10, Runs.NextSeed(), Everything);

        var (_, run, log) = await RunAsync(simulator, settings);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var shipments = log.Where(line => line.Outcome == LogOutcome.Planned).Select(line => line.ShipmentNumber!).ToList();
        shipments.Should().HaveCount(10);
        log.Count(line => line.Outcome == LogOutcome.Released).Should().Be(10);
        log.Count(line => line.Outcome == LogOutcome.Confirmed).Should().Be(10);
        log.Count(line => line.Outcome == LogOutcome.TrackingSent).Should().Be(50, "each Shipment reports collected, departed, arrived, out for delivery and delivered");
        log.Count(line => line.Outcome == LogOutcome.ProofOfDeliverySent).Should().Be(10);
        await Runs.WaitUntilAsync(async () => await AllAsync(shipments, async shipment =>
        {
            var timeline = (await fixture.TimelineAsync(shipment["shipmentNumber"]!.GetValue<string>(), Ct))!;
            return shipment["status"]!.GetValue<string>() == "Delivered" && timeline["proofsOfDelivery"]!.AsArray().Count == 1;
        }), Ct);

        foreach (var number in shipments)
        {
            var shipment = (await fixture.ShipmentAsync(number, Ct))!;
            var booking = shipment["carrierBookings"]!.AsArray().Single()!;
            booking["status"]!.GetValue<string>().Should().Be("Confirmed");
            booking["carrierBookingReference"]!.GetValue<string>().Should().StartWith($"{Prefixes.Single(prefix => booking["carrierBookingReference"]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal))}-SIM{run.Number}-");

            var timeline = (await fixture.TimelineAsync(number, Ct))!;
            // The tracking worker takes four messages at a time, so one Shipment's events can overtake each other on their way into the TMS:
            // what is asserted is that they are in order on the timeline and that the Shipment got to Delivered, not that none was passed over.
            var types = timeline["trackingEvents"]!.AsArray().Select(entry => entry!["type"]!.GetValue<string>()).ToList();
            types.Should().Contain("Delivered").And.BeInAscendingOrder(Comparer<string>.Create((left, right) => Array.IndexOf(Journey, left).CompareTo(Array.IndexOf(Journey, right))));
            var proofs = timeline["proofsOfDelivery"]!.AsArray();
            proofs.Should().ContainSingle().Which!["isCurrent"]!.GetValue<bool>().Should().BeTrue();
            proofs[0]!["condition"]!.GetValue<string>().Should().Be("Clean");
            proofs[0]!["files"]!.AsArray().Should().NotBeEmpty("the Proof of delivery came by claim check, and the TMS fetched its file");
        }
    }

    [Fact]
    public async Task TheAnswers_AreExactlyTheConfiguredShares_AndAMissingAnswerLeavesTheCarrierBookingRequested()
    {
        await using var simulator = await PricedSimulatorAsync();
        var shares = new CarrierShares(
            ReleasePercent: 80, ConfirmPercent: 50, DeclinePercent: 25, TrackedPercent: 0, ExceptionPercent: 0,
            DeliveredPercent: 0, ProofOfDeliveryPercent: 0, CancelPercent: 0);
        var settings = Runs.Carried(orders: 20, Runs.NextSeed(), shares);

        var (preview, _, log) = await RunAsync(simulator, settings);

        log.Count(line => line.Outcome == LogOutcome.Released).Should().Be(16);
        log.Count(line => line.Outcome == LogOutcome.Confirmed).Should().Be(8);
        log.Count(line => line.Outcome == LogOutcome.Declined).Should().Be(4);
        log.Count(line => line.Outcome == LogOutcome.NoAnswer).Should().Be(4);
        preview.Carriers.Counts.Should().BeEquivalentTo(new CarrierFunnelCounts(20, 16, 8, 4, 4, 0, 0, 0, 0, 0));

        foreach (var line in log.Where(line => line.Outcome is LogOutcome.Confirmed or LogOutcome.Declined or LogOutcome.NoAnswer))
        {
            var shipment = (await fixture.ShipmentAsync(line.ShipmentNumber!, Ct))!;
            var booking = shipment["carrierBookings"]!.AsArray().Single()!;
            booking["status"]!.GetValue<string>().Should().Be(line.Outcome switch
            {
                LogOutcome.Confirmed => "Confirmed",
                LogOutcome.Declined => "Rejected",
                _ => "Requested",
            });
            if (line.Outcome == LogOutcome.Declined)
            {
                booking["reasonCode"]!.GetValue<string>().Should().Be("NoCapacity");
            }
        }

        var notReleased = log.Where(line => line.Outcome == LogOutcome.Planned).Select(line => line.ShipmentNumber!)
            .Except(log.Where(line => line.Outcome == LogOutcome.Released).Select(line => line.ShipmentNumber!)).ToList();
        notReleased.Should().HaveCount(4);
        foreach (var number in notReleased)
        {
            var shipment = (await fixture.ShipmentAsync(number, Ct))!;
            shipment["status"]!.GetValue<string>().Should().Be("Planned");
            shipment["carrierBookings"]!.AsArray().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task TheTrackingAndDeliveries_AreExactlyTheConfiguredShares_AndTheShipmentsEndWhereTheirTrackingStopped()
    {
        await using var simulator = await PricedSimulatorAsync();
        var shares = new CarrierShares(
            ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 50, ExceptionPercent: 0,
            DeliveredPercent: 50, ProofOfDeliveryPercent: 60, CancelPercent: 0);
        var settings = Runs.Carried(orders: 20, Runs.NextSeed(), shares);

        var (preview, _, log) = await RunAsync(simulator, settings);

        preview.Carriers.Counts.Should().BeEquivalentTo(new CarrierFunnelCounts(20, 20, 20, 0, 0, 10, 0, 5, 3, 0));
        var tracked = log.Where(line => line.Outcome == LogOutcome.TrackingSent).GroupBy(line => line.ShipmentNumber!).ToDictionary(group => group.Key, group => group.Count());
        tracked.Should().HaveCount(10);
        log.Count(line => line.Outcome == LogOutcome.TrackingSent && line.Detail!.StartsWith("POD", StringComparison.Ordinal)).Should().Be(5);
        var proved = log.Where(line => line.Outcome == LogOutcome.ProofOfDeliverySent).Select(line => line.ShipmentNumber!).ToList();
        proved.Should().HaveCount(3);

        var shipments = log.Where(line => line.Outcome == LogOutcome.Planned).Select(line => line.ShipmentNumber!).ToList();
        await Runs.WaitUntilAsync(async () => await AllAsync(shipments, async shipment =>
        {
            var number = shipment["shipmentNumber"]!.GetValue<string>();
            var proofs = (await fixture.TimelineAsync(number, Ct))!["proofsOfDelivery"]!.AsArray().Count;
            return tracked.ContainsKey(number) == (shipment["status"]!.GetValue<string>() != "Booked") && proofs == (proved.Contains(number) ? 1 : 0);
        }), Ct);

        var statuses = new List<string>();
        foreach (var number in shipments)
        {
            var shipment = (await fixture.ShipmentAsync(number, Ct))!;
            var status = shipment["status"]!.GetValue<string>();
            statuses.Add(status);
            if (!tracked.ContainsKey(number))
            {
                status.Should().Be("Booked", "a Shipment that was confirmed and never tracked stays Booked");
            }
        }

        statuses.Count(status => status == "Delivered").Should().Be(5);
        statuses.Count(status => status == "Booked").Should().Be(10);
        statuses.Count(status => status is "Collected" or "InTransit").Should().Be(5, "the tracked Shipments that were not delivered stopped on the way");
        foreach (var number in shipments)
        {
            var timeline = (await fixture.TimelineAsync(number, Ct))!;
            timeline["proofsOfDelivery"]!.AsArray().Count.Should().Be(proved.Contains(number) ? 1 : 0);
        }
    }

    [Fact]
    public async Task TheExceptions_AreFoundByThePlanner_InAnEvenMix_AndTheFailedCollectionsAreCancelledAndResolved()
    {
        await using var simulator = await PricedSimulatorAsync();
        var shares = new CarrierShares(
            ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 100,
            DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 100);
        var settings = Runs.Carried(orders: 16, Runs.NextSeed(), shares);

        var (preview, run, log) = await RunAsync(simulator, settings);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        preview.Carriers.Counts.Exceptions.Should().Be(16);
        var found = log.Where(line => line.Outcome == LogOutcome.ExceptionFound).ToList();
        found.Should().HaveCount(16, "every Exception the Carriers' messages opened was found");
        found.GroupBy(line => line.Detail![(line.Detail!.IndexOf(": ", StringComparison.Ordinal) + 2)..]).Should()
            .HaveCount(4).And.OnlyContain(kind => kind.Count() == 4, "the kinds are dealt in turn");
        log.Should().NotContain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("didn't open in time", StringComparison.Ordinal));

        var collectionFailed = found.Where(line => line.Detail!.EndsWith(": CollectionFailed", StringComparison.Ordinal)).Select(line => line.ShipmentNumber!).ToList();
        log.Where(line => line.Outcome == LogOutcome.BookingCancelled).Select(line => line.ShipmentNumber!).Should().BeEquivalentTo(collectionFailed);
        log.Where(line => line.Outcome == LogOutcome.ExceptionResolved).Select(line => line.ShipmentNumber!).Should().BeEquivalentTo(collectionFailed);

        var open = await fixture.OpenExceptionsAsync(Ct);
        var openShipments = open.Select(exception => exception!["shipmentNumber"]!.GetValue<string>()).ToHashSet();
        openShipments.Should().NotContain(collectionFailed, "the Planner resolved those");
        openShipments.Should().Contain(found.Select(line => line.ShipmentNumber!).Except(collectionFailed), "the Planner leaves the others open");
    }

    [Fact]
    public async Task ACancelledCollection_LeavesTheShipmentWithoutAConfirmedCarrierBooking()
    {
        await using var simulator = await PricedSimulatorAsync();
        var shares = new CarrierShares(
            ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 100,
            DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 100);
        var settings = Runs.Carried(orders: 8, Runs.NextSeed(), shares);

        var (_, _, log) = await RunAsync(simulator, settings);

        var cancelled = log.Where(line => line.Outcome == LogOutcome.BookingCancelled).ToList();
        cancelled.Should().HaveCount(2);
        foreach (var line in cancelled)
        {
            var shipment = (await fixture.ShipmentAsync(line.ShipmentNumber!, Ct))!;
            shipment["status"]!.GetValue<string>().Should().Be("Planned", "the Shipment is back on Shipments to release");
            shipment["carrierBookings"]!.AsArray().Select(booking => booking!["status"]!.GetValue<string>()).Should().NotContain("Confirmed");
        }
    }

    [Fact]
    public async Task AReleaseTheTmsCannotPrice_IsLoggedWithItsCode_AndTheShipmentStaysPlanned_AndTheRunGoesOn()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(UnpricedShipper));
        var settings = Runs.Carried(orders: 6, Runs.NextSeed(), Everything);

        var (_, run, log) = await RunAsync(simulator, settings);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var refused = log.Where(line => line.Outcome == LogOutcome.PlanningRefused && line.Action == PlannerActions.Release).ToList();
        refused.Should().HaveCount(6, "every release was tried");
        refused.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId) && line.Detail != null
            && line.Detail.EndsWith("TMS: NoMatchingLane (422)", StringComparison.Ordinal));
        log.Where(Runs.IsCarrier).Should().BeEmpty("a Carrier answers only what was released to it");
        foreach (var number in log.Where(line => line.Outcome == LogOutcome.Planned).Select(line => line.ShipmentNumber!))
        {
            (await fixture.ShipmentAsync(number, Ct))!["status"]!.GetValue<string>().Should().Be("Planned");
        }
    }

    [Fact]
    public async Task ACarrierWithoutAPersona_IsNotedInTheLog_AndItsShipmentsStayPlanned()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper), SeedEdits.WithoutPersonasOf("CAR-0001"));
        var settings = Runs.Carried(orders: 12, Runs.NextSeed(), Everything);

        var (_, run, log) = await RunAsync(simulator, settings);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        log.Should().Contain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("Van Dijk Transport has no Persona", StringComparison.Ordinal));
        var released = log.Where(line => line.Outcome == LogOutcome.Released).Select(line => line.ShipmentNumber!).ToHashSet();
        var planned = log.Where(line => line.Outcome == LogOutcome.Planned).Select(line => line.ShipmentNumber!).ToList();
        var left = new List<string>();
        foreach (var number in planned)
        {
            var carrier = (await fixture.ShipmentAsync(number, Ct))!["carriedBy"]!["carrier"]!["id"]!.GetValue<string>();
            (carrier == "CAR-0001").Should().Be(!released.Contains(number), $"{number} is released unless its Carrier cannot be played");
            if (carrier == "CAR-0001")
            {
                left.Add(number);
            }
        }

        left.Should().NotBeEmpty("12 orders reach every one of the three Carriers");
        log.Where(Runs.IsCarrier).Select(line => line.ShipmentNumber).Should().NotIntersectWith(left);
    }

    [Fact]
    public async Task EveryCarrierAction_IsOnATrailOfItsOwn_AndTheShipmentsLinesAreFoundByItsNumber()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 6, Runs.NextSeed(), Everything);

        var (_, run, log) = await RunAsync(simulator, settings);

        var carrier = log.Where(Runs.IsCarrier).ToList();
        carrier.Should().NotBeEmpty();
        carrier.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId) && !string.IsNullOrWhiteSpace(line.ShipmentNumber)
            && !string.IsNullOrWhiteSpace(line.TransportOrderNumber));
        carrier.Select(line => line.CorrelationId).Should().OnlyHaveUniqueItems("every call has a Message trail of its own");
        carrier.Select(line => line.Actor).Distinct().Should().BeSubsetOf(["Van Dijk Transport", "Kessler Spedition", "PakketPunt Express"]);
        carrier.Select(line => line.Action).Distinct().Should().BeEquivalentTo([CarrierActions.Answer, CarrierActions.Track, CarrierActions.ProveDelivery]);

        var wanted = carrier.First().ShipmentNumber!;
        var byShipment = await simulator.Requests.ListLogAsync(new SimulationRunId(run.Id), new LogFilter(Text: wanted), Ct);
        byShipment.OrderBy(line => line.Sequence).Select(line => line.Outcome).Should().Equal(
            LogOutcome.Planned, LogOutcome.Released, LogOutcome.Confirmed,
            LogOutcome.TrackingSent, LogOutcome.TrackingSent, LogOutcome.TrackingSent, LogOutcome.TrackingSent, LogOutcome.TrackingSent,
            LogOutcome.ProofOfDeliverySent);

        var proof = byShipment.Single(line => line.Outcome == LogOutcome.ProofOfDeliverySent);
        var byCorrelationId = await simulator.Requests.ListLogAsync(new SimulationRunId(run.Id), new LogFilter(Text: proof.CorrelationId), Ct);
        byCorrelationId.Should().ContainSingle().Which.Outcome.Should().Be(LogOutcome.ProofOfDeliverySent);
        await Runs.WaitUntilAsync(async () => (await fixture.TimelineAsync(wanted, Ct))!["proofsOfDelivery"]!.AsArray().Count == 1, Ct);
        var recorded = (await fixture.TimelineAsync(wanted, Ct))!["proofsOfDelivery"]![0]!;
        recorded["correlationId"]!.GetValue<string>().Should().Be(proof.CorrelationId, "the log's correlation ID is the one the Partner API traced the call on");

        var counts = (await simulator.Requests.GetRunAsync(new SimulationRunId(run.Id), Ct)).Value.Outcomes;
        counts.Single(count => count.Outcome == LogOutcome.TrackingSent).Count.Should().Be(30);
    }

    [Fact]
    public async Task TheSameSeedTwice_MakesTheSameJourneys()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 20, Runs.NextSeed(), new CarrierShares());

        var (_, _, first) = await RunAsync(simulator, settings);
        var (_, _, second) = await RunAsync(simulator, settings);

        var firstJourneys = Journeys(first);
        firstJourneys.Should().NotBeEmpty();
        Journeys(second).Should().Equal(firstJourneys);
    }

    [Fact]
    public async Task AnotherSeed_MakesOtherJourneys()
    {
        await using var simulator = await PricedSimulatorAsync();
        var seed = Runs.NextSeed();

        var (_, _, first) = await RunAsync(simulator, Runs.Carried(orders: 20, seed, new CarrierShares()));
        var (_, _, second) = await RunAsync(simulator, Runs.Carried(orders: 20, seed + 1, new CarrierShares()));

        Journeys(second).Should().NotEqual(Journeys(first));
    }

    [Fact]
    public async Task APlanPreview_PromisesWhatTheCarriersDo()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 20, Runs.NextSeed(), new CarrierShares());

        var (preview, _, log) = await RunAsync(simulator, settings);

        var counts = preview.Carriers.Counts;
        counts.Shipments.Should().Be(log.Count(line => line.Outcome == LogOutcome.Planned));
        log.Count(line => line.Outcome == LogOutcome.Released).Should().Be(counts.Released);
        log.Count(line => line.Outcome == LogOutcome.Confirmed).Should().Be(counts.Confirmed);
        log.Count(line => line.Outcome == LogOutcome.Declined).Should().Be(counts.Declined);
        log.Count(line => line.Outcome == LogOutcome.NoAnswer).Should().Be(counts.NoAnswer);
        log.Count(line => line.Outcome == LogOutcome.ProofOfDeliverySent).Should().Be(counts.ProofsOfDelivery);
        log.Count(line => line.Outcome == LogOutcome.ExceptionFound).Should().Be(counts.Exceptions - log.Count(line => line.Outcome == LogOutcome.Info && line.Action.Contains("didn't open in time", StringComparison.Ordinal)));
        log.Count(line => line.Outcome == LogOutcome.BookingCancelled).Should().Be(counts.Cancelled);
    }

    // What each Carrier call and the Planner's follow-up came to, in order; the numbers the TMS and Norvia make up are left out.
    private static List<(string Action, LogOutcome Outcome)> Journeys(IReadOnlyList<LogLine> log) =>
        [.. log.Where(line => Runs.IsCarrier(line) || line.Outcome is LogOutcome.Released or LogOutcome.BookingCancelled or LogOutcome.ExceptionResolved)
            .OrderBy(line => line.Sequence).Select(line => (line.Action, line.Outcome))];

    private async Task<SimulatorHost> PricedSimulatorAsync() => await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));

    private async Task<bool> AllAsync(IEnumerable<string> shipmentNumbers, Func<JsonObject, Task<bool>> holds)
    {
        foreach (var number in shipmentNumbers)
        {
            if (await fixture.ShipmentAsync(number, Ct) is not { } shipment || !await holds(shipment))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<(PlanPreview Preview, RunSummary Run, IReadOnlyList<LogLine> Log)> RunAsync(SimulatorHost simulator, SimulationSettings settings)
    {
        var preview = (await simulator.Requests.PreviewAsync(settings, Ct)).Value;
        var started = await simulator.Requests.StartAsync(settings, Ct);
        var run = await simulator.Requests.FinishedAsync(started.Id, Ct);
        return (preview, run, await simulator.Requests.LogAsync(run.Id, Ct));
    }
}
