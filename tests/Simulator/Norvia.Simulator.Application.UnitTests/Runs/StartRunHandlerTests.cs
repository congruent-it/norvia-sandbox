using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Start;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Runs;

public sealed class StartRunHandlerTests
{
    private readonly IRepository<SimulationRun> _runs = Substitute.For<IRepository<SimulationRun>>();
    private readonly IRepository<RunSequence> _sequences = Substitute.For<IRepository<RunSequence>>();
    private readonly IRepository<LogEntry> _log = Substitute.For<IRepository<LogEntry>>();
    private readonly IWorldSource _world = Substitute.For<IWorldSource>();
    private readonly IRunExecution _execution = Substitute.For<IRunExecution>();
    private readonly FakeTimeProvider _time = new(TestData.Now);

    public StartRunHandlerTests()
    {
        _world.Load().Returns(TestData.World);
    }

    private StartRunHandler Handler() => new(_runs, _sequences, _log, _world, _execution, TestData.Limits, _time);

    [Fact]
    public async Task Handle_FirstRun_StartsRunOneWithThePlanAndExecutesIt()
    {
        var settings = TestData.Settings(orders: 5, seed: 8);

        var result = await Handler().Handle(new StartRunCommand(settings, "ngruson"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Number.Should().Be(1);
        result.Value.Planned.Should().Be(5);
        result.Value.IsActive.Should().BeTrue();
        await _runs.Received(1).AddAsync(
            Arg.Is<SimulationRun>(run => run.Number == 1 && run.StartedBy == "ngruson" && run.StartedAt == TestData.Now
                && run.Plan.Bookings.Count == 5 && run.Plan.Bookings[0].Reference == "SIM-1-1"),
            Arg.Any<CancellationToken>());
        _execution.Received(1).Start(Arg.Any<SimulationRunId>());
    }

    [Fact]
    public async Task Handle_TheSamePlanAsThePreview_SendsWhatWasShown()
    {
        var settings = TestData.Settings(orders: 12, bad: 25, seed: 99);
        SimulationRun? started = null;
        await _runs.AddAsync(Arg.Do<SimulationRun>(run => started = run), Arg.Any<CancellationToken>());

        await Handler().Handle(new StartRunCommand(settings, "ngruson"), CancellationToken.None);

        var preview = BookingPlanGenerator.Generate(settings, TestData.World, 1);
        started!.Plan.Should().BeEquivalentTo(preview);
    }

    [Fact]
    public async Task Handle_FirstRun_CreatesTheRunSequence()
    {
        await Handler().Handle(new StartRunCommand(TestData.Settings(), "ngruson"), CancellationToken.None);

        await _sequences.Received(1).AddAsync(Arg.Is<RunSequence>(sequence => sequence.Last == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LaterRun_TakesTheNextNumberFromTheSequence()
    {
        var sequence = RunSequence.Create();
        sequence.Take();
        sequence.Take();
        _sequences.FirstOrDefaultAsync(Arg.Any<ISpecification<RunSequence>>(), Arg.Any<CancellationToken>()).Returns(sequence);

        var result = await Handler().Handle(new StartRunCommand(TestData.Settings(), "ngruson"), CancellationToken.None);

        result.Value.Number.Should().Be(3);
        await _sequences.Received(1).UpdateAsync(sequence, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Start_LogsTheRunStartingAsItsFirstLine()
    {
        await Handler().Handle(new StartRunCommand(TestData.Settings(orders: 4, seed: 5), "ngruson"), CancellationToken.None);

        await _log.Received(1).AddAsync(
            Arg.Is<LogEntry>(line => line.Sequence == 1 && line.Outcome == LogOutcome.Info && line.Action.Contains("4 bookings", StringComparison.Ordinal) && line.Action.Contains("seed 5", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARunAlreadyActive_RefusesAndSendsNothing()
    {
        _runs.AnyAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().Handle(new StartRunCommand(TestData.Settings(), "ngruson"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        _execution.DidNotReceive().Start(Arg.Any<SimulationRunId>());
        await _runs.DidNotReceive().AddAsync(Arg.Any<SimulationRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_APlanOverTheCap_IsRefusedBeforeAnythingIsKept()
    {
        var result = await Handler().Handle(new StartRunCommand(TestData.Settings(orders: 51), "ngruson"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        _execution.DidNotReceive().Start(Arg.Any<SimulationRunId>());
        await _runs.DidNotReceive().AddAsync(Arg.Any<SimulationRun>(), Arg.Any<CancellationToken>());
        await _sequences.DidNotReceive().AddAsync(Arg.Any<RunSequence>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoShipperWithALane_IsInvalidAndKeepsNothing()
    {
        _world.Load().Returns(new Norvia.Simulator.Domain.Worlds.SimulationWorld([]));

        var result = await Handler().Handle(new StartRunCommand(TestData.Settings(), "ngruson"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        _execution.DidNotReceive().Start(Arg.Any<SimulationRunId>());
        await _sequences.DidNotReceive().AddAsync(Arg.Any<RunSequence>(), Arg.Any<CancellationToken>());
    }
}
