using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>The simulated Planner, as a run drives it: it assigns what Norvia accepted, through the TMS API, and logs what the TMS says.</summary>
public sealed class PlannerStageTests
{
    private readonly IRepository<SimulationRun> _runs = Substitute.For<IRepository<SimulationRun>>();
    private readonly IRepository<LogEntry> _log = Substitute.For<IRepository<LogEntry>>();
    private readonly IPartnerApi _api = Substitute.For<IPartnerApi>();
    private readonly ITmsApi _tms = Substitute.For<ITmsApi>();
    private readonly IDriverApp _drivers = Substitute.For<IDriverApp>();
    private readonly IWorldSource _world = Substitute.For<IWorldSource>();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly List<LogEntry> _lines = [];
    private readonly List<(string TransportOrder, Assignee To, string Key)> _assignments = [];
    private readonly List<(string Shipment, Assignee To, string Key)> _reassignments = [];

    public PlannerStageTests()
    {
        _world.Load().Returns(TestData.World);
        _log.AddAsync(Arg.Do<LogEntry>(line => _lines.Add(line)), Arg.Any<CancellationToken>()).Returns(call => call.Arg<LogEntry>());
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(call => new BookingAnswer(LogOutcome.Accepted, $"corr-{call.Arg<PlannedBooking>().Step}", $"TO-{call.Arg<PlannedBooking>().Step:0000000}", null));
        _tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FleetDay>?>(OpenWeek(capacityKg: 100_000, capacityPallets: 1_000)));
        // These tests are about the stages before the Trips: whatever stays with the Own fleet is not released here.
        _tms.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PlannerAnswer(LogOutcome.PlanningNotReceived, null, call.ArgAt<string>(0), "not released here"));
        _tms.AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _assignments.Add((call.ArgAt<string>(0), call.ArgAt<Assignee>(1), call.ArgAt<string>(2)));
            return new PlannerAnswer(LogOutcome.Planned, $"plan-{call.ArgAt<string>(0)}", $"SH-{call.ArgAt<string>(0)[3..]}", null);
        });
        _tms.ReassignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _reassignments.Add((call.ArgAt<string>(0), call.ArgAt<Assignee>(1), call.ArgAt<string>(2)));
            return new PlannerAnswer(LogOutcome.Reassigned, $"re-{call.ArgAt<string>(0)}", call.ArgAt<string>(0), null);
        });
    }

    private static IReadOnlyList<FleetDay> OpenWeek(decimal capacityKg, int capacityPallets) =>
    [
        .. Enumerable.Range(0, 10).Select(offset => new FleetDay(DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddDays(offset), capacityKg, capacityPallets, 0, 0)),
    ];

    private ExecuteRunHandler Handler() => new(_runs, _log, _api, _tms, _world, new SimulationLimits(50, 3), _time, new CarrierPacing(TimeSpan.Zero, 1), _drivers, new DriverPacing(TimeSpan.Zero, 1), Substitute.For<IErpApi>(), Substitute.For<IShipperInbox>());

    private async Task<SimulationRun> RunAsync(SimulationSettings settings)
    {
        var run = TestData.Run(settings);
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        (await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None)).IsSuccess.Should().BeTrue();
        return run;
    }

    [Fact]
    public async Task Handle_AnAssignedShare_AssignsThoseAcceptedOrdersInPlanOrder()
    {
        var run = await RunAsync(TestData.Settings(orders: 10, assigned: 50));

        _assignments.Should().HaveCount(5);
        _assignments.Select(assignment => assignment.TransportOrder)
            .Should().Equal(run.Plan.Choices.OrderBy(choice => choice.Step).Select(choice => $"TO-{choice.Step:0000000}"));
        var planned = _lines.Where(line => line.Outcome == LogOutcome.Planned).ToList();
        planned.Should().HaveCount(5);
        planned.Should().OnlyContain(line => line.Actor == "Planner" && line.Action == "Assign Transport Order");
    }

    [Fact]
    public async Task Handle_AnAssignment_LogsTheKeysTheTrailIsFoundBy()
    {
        var run = await RunAsync(TestData.Settings(orders: 4, assigned: 100));

        var step = run.Plan.Choices[0].Step;
        var line = _lines.Single(entry => entry.Outcome == LogOutcome.Planned && entry.TransportOrderNumber == $"TO-{step:0000000}");
        line.CorrelationId.Should().Be($"plan-TO-{step:0000000}");
        line.ShipmentNumber.Should().Be($"SH-{step:0000000}");
        line.ShipperReference.Should().Be($"SIM-1-{step}");
        line.At.Should().Be(TestData.Now);
    }

    [Fact]
    public async Task Handle_NoAssignedShare_NeverCallsTheTms()
    {
        await RunAsync(TestData.Settings(orders: 6, assigned: 0));

        await _tms.DidNotReceive().AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _tms.DidNotReceive().GetOwnFleetDaysAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyAcceptedAnswers_BecomeOrdersToAssign()
    {
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<PlannedBooking>().Step % 2 == 0
                ? new BookingAnswer(LogOutcome.AlreadyBooked, "c", "TO-0000099", null)
                : new BookingAnswer(LogOutcome.Accepted, "c", $"TO-{call.Arg<PlannedBooking>().Step:0000000}", null));

        await RunAsync(TestData.Settings(orders: 10, assigned: 100));

        _assignments.Should().NotBeEmpty();
        _assignments.Should().OnlyContain(assignment => assignment.TransportOrder != "TO-0000099");
    }

    [Fact]
    public async Task Handle_OwnFleetWithRoom_GetsTheOrder()
    {
        await RunAsync(TestData.Settings(orders: 6, assigned: 100, ownFleet: 100));

        _assignments.Should().HaveCount(6).And.OnlyContain(assignment => assignment.To.IsOwnFleet);
    }

    [Fact]
    public async Task Handle_OwnFleetFull_SendsTheOrderToAnActiveCarrier()
    {
        _tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FleetDay>?>(OpenWeek(capacityKg: 1, capacityPallets: 0)));

        await RunAsync(TestData.Settings(orders: 6, assigned: 100, ownFleet: 100));

        _assignments.Should().HaveCount(6);
        _assignments.Should().OnlyContain(assignment => !assignment.To.IsOwnFleet && assignment.To.CarrierId != "CAR-0004");
        _lines.Where(line => line.Outcome == LogOutcome.Planned).Should().OnlyContain(line => line.Detail!.StartsWith("Own fleet has no room that day"));
    }

    [Fact]
    public async Task Handle_OwnFleetRunsOutHalfwayThrough_TheRestGoToCarriers()
    {
        // Room for about two orders: the Planner's own tally of what it put on each day, not only what the TMS reported.
        var day = new FleetDay(DateOnly.FromDateTime(TestData.Now.UtcDateTime), 1, 0, 0, 0);
        _tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FleetDay>?>(
            [.. Enumerable.Range(0, 10).Select(offset => day with { Date = day.Date.AddDays(offset), CapacityKg = 10_000, CapacityPalletPlaces = 1_000 })]));
        var settings = TestData.Settings(orders: 6, assigned: 100, ownFleet: 100);
        var heavy = TestData.Run(settings).Plan.Bookings.Max(booking => booking.TotalWeightKg);
        _tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FleetDay>?>(
            [.. Enumerable.Range(0, 10).Select(offset => new FleetDay(day.Date.AddDays(offset), heavy, 1_000, 0, 0))]));

        await RunAsync(settings);

        _assignments.Count(assignment => assignment.To.IsOwnFleet).Should().BeLessThan(6);
        _assignments.Count(assignment => !assignment.To.IsOwnFleet).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_TheTmsCantTellTheCapacity_AssignsToCarriersAndSaysSo()
    {
        _tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FleetDay>?>(null));

        await RunAsync(TestData.Settings(orders: 4, assigned: 100, ownFleet: 100));

        _assignments.Should().HaveCount(4).And.OnlyContain(assignment => !assignment.To.IsOwnFleet);
        _lines.Should().Contain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("couldn't tell the Own fleet's capacity"));
    }

    [Fact]
    public async Task Handle_ARefusal_IsLoggedWithItsCodeAndCorrelationIdAndTheRunGoesOn()
    {
        _tms.AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PlannerAnswer(LogOutcome.PlanningRefused, $"refused-{call.ArgAt<string>(0)}", null, "TransportOrderAlreadyPlanned (409)"));

        var run = await RunAsync(TestData.Settings(orders: 6, assigned: 100));

        var refused = _lines.Where(line => line.Outcome == LogOutcome.PlanningRefused).ToList();
        refused.Should().HaveCount(6);
        refused[0].Detail.Should().Contain("TransportOrderAlreadyPlanned (409)");
        refused[0].CorrelationId.Should().StartWith("refused-TO-");
        refused[0].ShipmentNumber.Should().BeNull();
        run.Status.Should().Be(SimulationRunStatus.Completed);
        _reassignments.Should().BeEmpty("a refused assignment has no Shipment to reassign");
    }

    [Fact]
    public async Task Handle_TheTmsNotReached_IsLoggedAndTheRunGoesOn()
    {
        _tms.AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlannerAnswer(LogOutcome.PlanningNotReceived, null, null, "The TMS API couldn't be reached"));

        var run = await RunAsync(TestData.Settings(orders: 3, assigned: 100));

        _lines.Count(line => line.Outcome == LogOutcome.PlanningNotReceived).Should().Be(3);
        run.Status.Should().Be(SimulationRunStatus.Completed);
    }

    [Fact]
    public async Task Handle_AReassignShare_ReassignsThoseShipmentsToSomeoneElse()
    {
        var run = await RunAsync(TestData.Settings(orders: 10, assigned: 100, ownFleet: 100, reassign: 30));

        _reassignments.Should().HaveCount(3);
        foreach (var (shipment, to, _) in _reassignments)
        {
            var step = int.Parse(shipment[3..], System.Globalization.CultureInfo.InvariantCulture);
            _assignments.Single(assignment => assignment.TransportOrder == $"TO-{step:0000000}").To.Should().NotBe(to, "the TMS refuses an unchanged assignment");
        }

        _lines.Count(line => line.Outcome == LogOutcome.Reassigned && line.Action == "Reassign Shipment").Should().Be(3);
        run.Plan.Choices.Count(choice => choice.Reassign).Should().Be(3);
    }

    [Fact]
    public async Task Handle_TheKeysOfTheCalls_AreTheSameForARetry()
    {
        await RunAsync(TestData.Settings(orders: 4, assigned: 100, reassign: 100));

        _assignments.Select(assignment => assignment.Key).Should().OnlyContain(key => key.StartsWith("SIM-1-") && key.EndsWith("-assign"));
        _reassignments.Select(reassignment => reassignment.Key).Should().OnlyContain(key => key.StartsWith("SIM-1-") && key.EndsWith("-reassign"));
    }

    [Fact]
    public async Task Handle_TheSameSeedAndTheSameTmsState_GivesTheSameChoices()
    {
        await RunAsync(TestData.Settings(orders: 12, assigned: 75, ownFleet: 50, reassign: 25, seed: 5));
        var first = _assignments.Select(assignment => (assignment.TransportOrder, assignment.To)).ToList();
        var firstReassigned = _reassignments.Select(reassignment => (reassignment.Shipment, reassignment.To)).ToList();
        _assignments.Clear();
        _reassignments.Clear();

        await RunAsync(TestData.Settings(orders: 12, assigned: 75, ownFleet: 50, reassign: 25, seed: 5));

        _assignments.Select(assignment => (assignment.TransportOrder, assignment.To)).Should().Equal(first);
        _reassignments.Select(reassignment => (reassignment.Shipment, reassignment.To)).Should().Equal(firstReassigned);
    }

    [Fact]
    public async Task Handle_ARunCancelledWhileBooking_NeverStartsPlanning()
    {
        var run = TestData.Run(TestData.Settings(orders: 6, assigned: 100));
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        using var stop = new CancellationTokenSource();
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            return Task.FromResult(new BookingAnswer(LogOutcome.Accepted, "c", "TO-1", null));
        });

        await Handler().Handle(new ExecuteRunCommand(run.Id, stop.Token), CancellationToken.None);

        _assignments.Should().BeEmpty();
        _lines.Last().Action.Should().Contain("cancelled");
    }

    [Fact]
    public async Task Handle_ARunCancelledWhilePlanning_PlansNoMoreAndSaysSo()
    {
        var run = TestData.Run(TestData.Settings(orders: 6, assigned: 100));
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        using var stop = new CancellationTokenSource();
        _tms.AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            return Task.FromResult(new PlannerAnswer(LogOutcome.Planned, "p", "SH-1", null));
        });

        await Handler().Handle(new ExecuteRunCommand(run.Id, stop.Token), CancellationToken.None);

        await _tms.Received(1).AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _lines.Last().Action.Should().Contain("cancelled");
        run.Status.Should().Be(SimulationRunStatus.Running);
    }

    [Fact]
    public async Task Handle_Finished_CountsTheOrdersForThePlanner()
    {
        await RunAsync(TestData.Settings(orders: 6, assigned: 50));

        _lines.Last().Action.Should().Contain("finished").And.Contain("3 Transport Orders for the Planner");
    }
}
