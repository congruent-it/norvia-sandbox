using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>The Carriers after the Planner: the Planner releases what went to a Carrier, and each Carrier answers, tracks and delivers as its Persona.</summary>
public sealed class CarrierStageTests
{
    private static readonly CarrierShares Everything = new(
        ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 0,
        DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 0);

    private readonly IRepository<SimulationRun> _runs = Substitute.For<IRepository<SimulationRun>>();
    private readonly IRepository<LogEntry> _log = Substitute.For<IRepository<LogEntry>>();
    private readonly IPartnerApi _api = Substitute.For<IPartnerApi>();
    private readonly ITmsApi _tms = Substitute.For<ITmsApi>();
    private readonly IWorldSource _world = Substitute.For<IWorldSource>();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly List<LogEntry> _lines = [];
    private readonly List<(string Shipment, string Key)> _releases = [];
    private readonly List<(SimulatedCarrier Carrier, CarrierAnswerRequest Answer)> _answers = [];
    private readonly List<(SimulatedCarrier Carrier, CarrierTrackingRequest Tracking)> _tracking = [];
    private readonly List<(SimulatedCarrier Carrier, CarrierProofRequest Proof)> _proofs = [];
    private readonly List<(string Exception, string Reason, string Key)> _cancellations = [];
    private readonly List<(string Exception, string Remark)> _resolutions = [];
    private readonly List<string> _openedOnShipments = [];

    public CarrierStageTests()
    {
        _world.Load().Returns(TestData.World);
        _log.AddAsync(Arg.Do<LogEntry>(line => _lines.Add(line)), Arg.Any<CancellationToken>()).Returns(call => call.Arg<LogEntry>());
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(call => new BookingAnswer(LogOutcome.Accepted, $"corr-{call.Arg<PlannedBooking>().Step}", $"TO-{call.Arg<PlannedBooking>().Step:0000000}", null));
        _tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FleetDay>?>(
            [.. Enumerable.Range(0, 10).Select(offset => new FleetDay(DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddDays(offset), 100_000, 1_000, 0, 0))]));
        _tms.AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PlannerAnswer(LogOutcome.Planned, $"plan-{call.ArgAt<string>(0)}", $"SH-{call.ArgAt<string>(0)[3..]}", null));
        _tms.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _releases.Add((call.ArgAt<string>(0), call.ArgAt<string>(1)));
            return new PlannerAnswer(LogOutcome.Released, $"rel-{call.ArgAt<string>(0)}", call.ArgAt<string>(0), null, CarrierBookingNumber: $"CB-{call.ArgAt<string>(0)[3..]}");
        });
        _api.AnswerBookingAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierAnswerRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _answers.Add((call.Arg<SimulatedCarrier>(), call.Arg<CarrierAnswerRequest>()));
            return new CarrierCallAnswer(call.Arg<CarrierAnswerRequest>().Confirm ? LogOutcome.Confirmed : LogOutcome.Declined, $"ans-{call.Arg<CarrierAnswerRequest>().CarrierBookingNumber}", null);
        });
        _api.SendTrackingAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierTrackingRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _tracking.Add((call.Arg<SimulatedCarrier>(), call.Arg<CarrierTrackingRequest>()));
            return new CarrierCallAnswer(LogOutcome.TrackingSent, $"trk-{call.Arg<CarrierTrackingRequest>().EventId}", null);
        });
        _api.SendProofOfDeliveryAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierProofRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _proofs.Add((call.Arg<SimulatedCarrier>(), call.Arg<CarrierProofRequest>()));
            return new CarrierCallAnswer(LogOutcome.ProofOfDeliverySent, $"pod-{call.Arg<CarrierProofRequest>().DocumentId}", null);
        });
        _tms.GetOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<OpenExceptionSummary>?>(
            [.. _tracking.Where(sent => sent.Tracking.Event is CarrierEvent.CollectionFailed or CarrierEvent.DeliveryFailed or CarrierEvent.Damaged)
                .Select(sent => (Shipment: ShipmentOf(sent.Tracking.CarrierBookingNumber), sent.Tracking.Event))
                .Where(opened => _openedOnShipments.Contains(opened.Shipment))
                .Select(opened => new OpenExceptionSummary($"EXC-{opened.Shipment[3..]}", opened.Shipment, opened.Event.ToString()))]));
        _tms.CancelCarrierBookingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _cancellations.Add((call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2)));
            return new PlannerAnswer(LogOutcome.BookingCancelled, $"can-{call.ArgAt<string>(0)}", $"SH-{call.ArgAt<string>(0)[4..]}", null);
        });
        _tms.ResolveReplannedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _resolutions.Add((call.ArgAt<string>(0), call.ArgAt<string>(1)));
            return new PlannerAnswer(LogOutcome.ExceptionResolved, $"res-{call.ArgAt<string>(0)}", $"SH-{call.ArgAt<string>(0)[4..]}", null);
        });
    }

    private static string ShipmentOf(string carrierBookingNumber) => $"SH-{carrierBookingNumber[3..]}";

    private ExecuteRunHandler Handler(CarrierPacing? pacing = null) => new(_runs, _log, _api, _tms, _world, new SimulationLimits(50, 3), _time, pacing ?? new CarrierPacing(TimeSpan.Zero, 3));

    private async Task<SimulationRun> RunAsync(CarrierShares shares, int orders = 6, int ownFleet = 0, CancellationTokenSource? stop = null)
    {
        var run = TestData.Run(TestData.Settings(orders: orders, assigned: 100, ownFleet: ownFleet, carriers: shares));
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        (await Handler().Handle(new ExecuteRunCommand(run.Id, stop?.Token ?? CancellationToken.None), CancellationToken.None)).IsSuccess.Should().BeTrue();
        return run;
    }

    [Fact]
    public async Task Handle_TheShipmentsOfCarriers_AreReleasedInPlanOrderUnderTheirOwnKeys()
    {
        var run = await RunAsync(Everything);

        _releases.Select(release => release.Shipment).Should().Equal(run.Plan.Choices.OrderBy(choice => choice.Step).Select(choice => $"SH-{choice.Step:0000000}"));
        _releases.Select(release => release.Key).Should().Equal(run.Plan.Choices.OrderBy(choice => choice.Step).Select(choice => $"SIM-1-{choice.Step}-release"));
        _lines.Where(line => line.Outcome == LogOutcome.Released).Should().HaveCount(6).And.OnlyContain(line => line.Actor == "Planner" && line.Action == "Release Shipment");
    }

    [Fact]
    public async Task Handle_AReleaseShare_ReleasesExactlyThatShareOfTheCarrierShipments()
    {
        await RunAsync(Everything with { ReleasePercent = 50 }, orders: 6);

        _releases.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_NoReleaseShare_NeverReleasesAnything()
    {
        await RunAsync(Everything with { ReleasePercent = 0 });

        await _tms.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _answers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShipmentsAssignedToOwnFleet_AreLeftForTheTrips()
    {
        await RunAsync(Everything, orders: 6, ownFleet: 100);

        _releases.Should().BeEmpty();
        _answers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ARelease_LogsTheCarrierBookingAndTheKeysTheTrailIsFoundBy()
    {
        var run = await RunAsync(Everything, orders: 2);

        var step = run.Plan.Choices.OrderBy(choice => choice.Step).First().Step;
        var line = _lines.Single(entry => entry.Outcome == LogOutcome.Released && entry.ShipmentNumber == $"SH-{step:0000000}");
        line.CorrelationId.Should().Be($"rel-SH-{step:0000000}");
        line.TransportOrderNumber.Should().Be($"TO-{step:0000000}");
        line.ShipperReference.Should().Be($"SIM-1-{step}");
        line.Detail.Should().Contain($"CB-{step:0000000}");
    }

    [Fact]
    public async Task Handle_AReleaseTheTmsRefuses_IsLoggedAndTheCarrierIsNeverAsked()
    {
        _tms.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlannerAnswer(LogOutcome.PlanningRefused, "c", "SH-1", "NoRateCard (422)", "NoRateCard"));

        await RunAsync(Everything, orders: 3);

        _lines.Where(line => line.Action == "Release Shipment").Should().OnlyContain(line => line.Outcome == LogOutcome.PlanningRefused && line.Detail!.Contains("NoRateCard"));
        _answers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ACarrierThatConfirms_AnswersAsItsPersonaWithABookingReferenceOfItsOwn()
    {
        var run = await RunAsync(Everything, orders: 4);

        _answers.Should().HaveCount(4);
        foreach (var (carrier, answer) in _answers)
        {
            answer.Confirm.Should().BeTrue();
            carrier.HasPersona.Should().BeTrue();
            answer.BookingReference.Should().StartWith($"{carrier.BookingReferencePrefix}-SIM{run.Number}-");
            answer.DeclineReason.Should().BeNull();
            answer.CarrierBookingNumber.Should().StartWith("CB-");
        }

        var confirmations = _lines.Where(line => line.Outcome == LogOutcome.Confirmed).ToList();
        confirmations.Should().HaveCount(4).And.OnlyContain(line => line.Action == "Answer Carrier booking" && line.CorrelationId!.StartsWith("ans-CB-", StringComparison.Ordinal));
        confirmations.Select(line => line.Actor).Should().OnlyContain(actor => actor == "Van Dijk Transport" || actor == "Kessler Spedition");
    }

    [Fact]
    public async Task Handle_ACarrierThatDeclines_GivesAReasonAndTheShipmentGoesNoFurther()
    {
        await RunAsync(Everything with { ConfirmPercent = 0, DeclinePercent = 100 }, orders: 4);

        _answers.Should().HaveCount(4).And.OnlyContain(sent => !sent.Answer.Confirm && sent.Answer.BookingReference == null && sent.Answer.DeclineReason == "NO_CAPACITY");
        _lines.Where(line => line.Outcome == LogOutcome.Declined).Should().HaveCount(4);
        _tracking.Should().BeEmpty();
        _proofs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ACarrierThatGivesNoAnswer_IsLoggedAndTheBookingStaysRequested()
    {
        await RunAsync(Everything with { ConfirmPercent = 0, DeclinePercent = 0 }, orders: 4);

        _answers.Should().BeEmpty();
        var silent = _lines.Where(line => line.Outcome == LogOutcome.NoAnswer).ToList();
        silent.Should().HaveCount(4).And.OnlyContain(line => line.Action == "Answer Carrier booking" && line.CorrelationId == null && line.ShipmentNumber != null);
        _tracking.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnAnswerNorviaRefuses_IsLoggedAndTheShipmentGoesNoFurther()
    {
        _api.AnswerBookingAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierAnswerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CarrierCallAnswer(LogOutcome.CarrierRefused, "c", "BOOKING_NOT_OPEN (409)"));

        await RunAsync(Everything, orders: 3);

        _lines.Where(line => line.Action == "Answer Carrier booking").Should().HaveCount(3).And.OnlyContain(line => line.Outcome == LogOutcome.CarrierRefused && line.Detail!.Contains("BOOKING_NOT_OPEN"));
        _tracking.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ConfirmedShipments_ReportEveryTrackingEventInOrderAsTheCarriersPersona()
    {
        await RunAsync(Everything, orders: 2);

        foreach (var perShipment in _tracking.GroupBy(sent => sent.Tracking.CarrierBookingNumber))
        {
            perShipment.Select(sent => sent.Tracking.Event).Should().Equal(
                CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Arrived, CarrierEvent.OutForDelivery, CarrierEvent.Delivered);
            perShipment.Select(sent => sent.Tracking.At).Should().BeInAscendingOrder().And.OnlyContain(at => at <= TestData.Now);
            perShipment.Select(sent => sent.Tracking.BookingReference).Distinct().Should().ContainSingle();
        }

        _tracking.Select(sent => sent.Tracking.EventId).Should().OnlyHaveUniqueItems();
        _lines.Where(line => line.Outcome == LogOutcome.TrackingSent).Should().HaveCount(10).And.OnlyContain(line => line.Action == "Send tracking" && line.CorrelationId!.StartsWith("trk-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_TrackingMatchesWhatTheCarrierConfirmed()
    {
        await RunAsync(Everything, orders: 3);

        foreach (var (carrier, answer) in _answers)
        {
            _tracking.Where(sent => sent.Tracking.CarrierBookingNumber == answer.CarrierBookingNumber)
                .Should().NotBeEmpty().And.OnlyContain(sent => sent.Tracking.BookingReference == answer.BookingReference && sent.Carrier == carrier);
        }
    }

    [Fact]
    public async Task Handle_ADeliveredShipment_GetsItsProofOfDeliveryAfterTheDeliveredEvent()
    {
        await RunAsync(Everything, orders: 3);

        _proofs.Should().HaveCount(3).And.OnlyContain(sent => !sent.Proof.Reservations && sent.Proof.Remark == null && sent.Proof.ReceivedBy.Length > 0);
        _lines.Where(line => line.Outcome == LogOutcome.ProofOfDeliverySent).Should().HaveCount(3).And.OnlyContain(line => line.Action == "Send Proof of delivery");
        foreach (var (_, proof) in _proofs)
        {
            _tracking.Single(sent => sent.Tracking.CarrierBookingNumber == proof.CarrierBookingNumber && sent.Tracking.Event == CarrierEvent.Delivered).Tracking.At
                .Should().BeOnOrBefore(proof.At);
        }
    }

    [Fact]
    public async Task Handle_AShipmentThatStopsEnRoute_ReportsNoDeliveryAndNoProof()
    {
        await RunAsync(Everything with { DeliveredPercent = 0 }, orders: 4);

        _tracking.Should().NotBeEmpty().And.NotContain(sent => sent.Tracking.Event == CarrierEvent.Delivered);
        _proofs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ATrackingMessageNorviaDoesNotReceive_StopsTheShipmentsReports()
    {
        _api.SendTrackingAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierTrackingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CarrierCallAnswer(LogOutcome.CarrierNotReceived, null, "The call didn't reach the Norvia Partner API."));

        await RunAsync(Everything, orders: 3);

        _lines.Where(line => line.Action == "Send tracking").Should().HaveCount(3).And.OnlyContain(line => line.Outcome == LogOutcome.CarrierNotReceived);
        _proofs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DamageAndReservations_AreReportedAndTheShipmentIsDeliveredWithReservations()
    {
        await RunAsync(Everything with { ExceptionPercent = 100 }, orders: 12);

        _tracking.Should().Contain(sent => sent.Tracking.Event == CarrierEvent.Damaged);
        _tracking.Should().Contain(sent => sent.Tracking.Event == CarrierEvent.CollectionFailed);
        _tracking.Should().Contain(sent => sent.Tracking.Event == CarrierEvent.DeliveryFailed);
        _proofs.Should().Contain(sent => sent.Proof.Reservations && sent.Proof.Remark != null);
    }

    [Fact]
    public async Task Handle_ARunCancelledMidway_StopsBeforeTheNextShipmentAndSaysSo()
    {
        using var stop = new CancellationTokenSource();
        _tms.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _releases.Add((call.ArgAt<string>(0), call.ArgAt<string>(1)));
            stop.Cancel();
            return new PlannerAnswer(LogOutcome.Released, "c", call.ArgAt<string>(0), null, CarrierBookingNumber: $"CB-{call.ArgAt<string>(0)[3..]}");
        });

        var run = await RunAsync(Everything, orders: 6, stop: stop);

        _releases.Should().HaveCount(1);
        run.Status.Should().Be(SimulationRunStatus.Running);
        _lines.Should().Contain(line => line.Action == "Run cancelled: nothing more is sent");
    }

    [Fact]
    public async Task Handle_AFailedCollection_OpensAnExceptionThePlannerFindsAndCancelsAndResolvesInTheCancelShare()
    {
        var run = TestData.Run(TestData.Settings(orders: 12, assigned: 100, ownFleet: 0, carriers: Everything with { ExceptionPercent = 100, CancelPercent = 100 }));
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        _tracking.Clear();
        foreach (var choice in run.Plan.Choices)
        {
            _openedOnShipments.Add($"SH-{choice.Step:0000000}");
        }

        await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None);

        var failedCollections = _tracking.Where(sent => sent.Tracking.Event == CarrierEvent.CollectionFailed).Select(sent => ShipmentOf(sent.Tracking.CarrierBookingNumber)).ToList();
        failedCollections.Should().NotBeEmpty();
        _cancellations.Select(cancellation => cancellation.Exception).Should().BeEquivalentTo(failedCollections.Select(shipment => $"EXC-{shipment[3..]}"));
        _cancellations.Should().OnlyContain(cancellation => cancellation.Reason.Length > 0 && cancellation.Key.StartsWith("SIM-1-", StringComparison.Ordinal) && cancellation.Key.EndsWith("-cancel", StringComparison.Ordinal));
        _resolutions.Select(resolution => resolution.Exception).Should().BeEquivalentTo(_cancellations.Select(cancellation => cancellation.Exception));
        _lines.Where(line => line.Outcome == LogOutcome.ExceptionFound).Should().HaveCount(_openedOnShipments.Count(shipment => ExceptionKindOf(shipment) is not null));
        _lines.Where(line => line.Outcome == LogOutcome.BookingCancelled).Should().HaveCount(failedCollections.Count).And.OnlyContain(line => line.Action == "Cancel Carrier booking");
        _lines.Where(line => line.Outcome == LogOutcome.ExceptionResolved).Should().HaveCount(failedCollections.Count);
    }

    private CarrierEvent? ExceptionKindOf(string shipment) =>
        _tracking.Where(sent => ShipmentOf(sent.Tracking.CarrierBookingNumber) == shipment)
            .Select(sent => (CarrierEvent?)sent.Tracking.Event)
            .FirstOrDefault(carrierEvent => carrierEvent is CarrierEvent.CollectionFailed or CarrierEvent.DeliveryFailed or CarrierEvent.Damaged);

    [Fact]
    public async Task Handle_NoCancelShare_LeavesTheExceptionsOpen()
    {
        var run = TestData.Run(TestData.Settings(orders: 12, assigned: 100, ownFleet: 0, carriers: Everything with { ExceptionPercent = 100, CancelPercent = 0 }));
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        foreach (var choice in run.Plan.Choices)
        {
            _openedOnShipments.Add($"SH-{choice.Step:0000000}");
        }

        await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None);

        _cancellations.Should().BeEmpty();
        _resolutions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnExceptionThatNeverOpens_IsNotedAfterTheAttemptsAreUsed()
    {
        var run = await RunAsync(Everything with { ExceptionPercent = 100, CancelPercent = 100 }, orders: 6);

        await _tms.Received(3).GetOpenExceptionsAsync(Arg.Any<CancellationToken>());
        _cancellations.Should().BeEmpty();
        _lines.Should().Contain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("didn't open"));
        run.Status.Should().Be(SimulationRunStatus.Completed);
    }

    [Fact]
    public async Task Handle_TheListOfOpenExceptionsUnavailable_IsNotedAndTheRunStillFinishes()
    {
        _tms.GetOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OpenExceptionSummary>?>(null));

        var run = await RunAsync(Everything with { ExceptionPercent = 100, CancelPercent = 100 }, orders: 6);

        _lines.Should().Contain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("Open Exceptions"));
        run.Status.Should().Be(SimulationRunStatus.Completed);
    }

    [Fact]
    public async Task Handle_ACarrierWithoutAPersona_IsNotedAndItsShipmentsAreNotReleased()
    {
        _world.Load().Returns(new SimulationWorld(TestData.World.Shippers, [new SimulatedCarrier("CAR-0001", "Van Dijk Transport", true)]));

        await RunAsync(Everything, orders: 3);

        _releases.Should().BeEmpty();
        _lines.Should().Contain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("no Persona"));
    }

    [Fact]
    public async Task Handle_TheRunFinishesWithTheFunnelInItsLastLine()
    {
        var run = await RunAsync(Everything with { ReleasePercent = 50 }, orders: 6);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        _lines.Last().Action.Should().Contain("3 released").And.Contain("3 confirmed").And.Contain("3 delivered");
    }
}
