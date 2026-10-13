using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Log.List;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Abandon;
using Norvia.Simulator.Application.Runs.Cancel;
using Norvia.Simulator.Application.Runs.ClearHistory;
using Norvia.Simulator.Application.Runs.List;
using Norvia.Simulator.Application.Runs.Open;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Runs;

public sealed class RunQueriesAndCommandsTests
{
    private readonly IRepository<SimulationRun> _runs = Substitute.For<IRepository<SimulationRun>>();
    private readonly IRepository<LogEntry> _log = Substitute.For<IRepository<LogEntry>>();
    private readonly IRunExecution _execution = Substitute.For<IRunExecution>();
    private readonly FakeTimeProvider _time = new(TestData.Now.AddMinutes(5));

    private SimulationRun GivenRun(SimulationRun? run = null)
    {
        run ??= TestData.Run();
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        return run;
    }

    [Fact]
    public async Task Cancel_ARunningRun_IsCancelledAndItsExecutionStopped()
    {
        var run = GivenRun();

        var result = await new CancelRunHandler(_runs, _execution, _time).Handle(new CancelRunCommand(run.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        run.Status.Should().Be(SimulationRunStatus.Cancelled);
        run.FinishedAt.Should().Be(_time.GetUtcNow());
        await _runs.Received(1).UpdateAsync(run, Arg.Any<CancellationToken>());
        _execution.Received(1).Cancel(run.Id);
    }

    [Fact]
    public async Task Cancel_AFinishedRun_IsAConflictAndStopsNothing()
    {
        var run = GivenRun();
        run.Complete(TestData.Now);

        var result = await new CancelRunHandler(_runs, _execution, _time).Handle(new CancelRunCommand(run.Id), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        _execution.DidNotReceive().Cancel(Arg.Any<SimulationRunId>());
    }

    [Fact]
    public async Task Cancel_NoSuchRun_IsNotFound()
    {
        var result = await new CancelRunHandler(_runs, _execution, _time).Handle(new CancelRunCommand(SimulationRunId.New()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ClearHistory_NoActiveRun_RemovesTheFinishedRunsAndTheirLogs()
    {
        var finished = new List<SimulationRun> { TestData.Run() };
        var lines = new List<LogEntry> { LogEntry.Note(finished[0].Id, 1, TestData.Now, "x") };
        _runs.ListAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(finished);
        _log.ListAsync(Arg.Any<ISpecification<LogEntry>>(), Arg.Any<CancellationToken>()).Returns(lines);

        var result = await new ClearHistoryHandler(_runs, _log).Handle(new ClearHistoryCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _runs.Received(1).DeleteRangeAsync(finished, Arg.Any<CancellationToken>());
        await _log.Received(1).DeleteRangeAsync(lines, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearHistory_ARunActive_ClearsNothing()
    {
        _runs.AnyAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await new ClearHistoryHandler(_runs, _log).Handle(new ClearHistoryCommand(), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        await _runs.DidNotReceive().DeleteRangeAsync(Arg.Any<IEnumerable<SimulationRun>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Abandon_RunsStillRunning_AreFailed()
    {
        var stale = TestData.Run();
        _runs.ListAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns([stale]);

        var count = await new AbandonActiveRunsHandler(_runs, _time).Handle(new AbandonActiveRunsCommand(), CancellationToken.None);

        count.Should().Be(1);
        stale.Status.Should().Be(SimulationRunStatus.Failed);
        await _runs.Received(1).UpdateRangeAsync(Arg.Any<IEnumerable<SimulationRun>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_GivesEachRunWithHowManyBookingsItSent()
    {
        var run = TestData.Run();
        var reads = Substitute.For<IReadRepository<SimulationRun>>();
        reads.ListAsync(Arg.Any<ISpecification<SimulationRun, RunListRow>>(), Arg.Any<CancellationToken>())
            .Returns([new RunListRow(run.Id.Value, run.Number, run.Status, run.StartedAt, run.FinishedAt, run.StartedBy, run.Seed, run.PresetName, run.Mode, run.PlannedCount)]);
        var lines = Substitute.For<IReadRepository<LogEntry>>();
        lines.CountAsync(Arg.Any<ISpecification<LogEntry>>(), Arg.Any<CancellationToken>()).Returns(4);

        var list = await new ListRunsHandler(reads, lines).Handle(new ListRunsQuery(), CancellationToken.None);

        var summary = list.Should().ContainSingle().Subject;
        summary.Number.Should().Be(1);
        summary.Planned.Should().Be(6);
        summary.Sent.Should().Be(4);
        summary.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Open_ARun_CountsTheAnswersAndNamesTheActors()
    {
        var run = TestData.Run();
        var reads = Substitute.For<IReadRepository<SimulationRun>>();
        reads.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        var lines = Substitute.For<IReadRepository<LogEntry>>();
        lines.ListAsync(Arg.Any<ISpecification<LogEntry, LogOutcome>>(), Arg.Any<CancellationToken>())
            .Returns([LogOutcome.Accepted, LogOutcome.Accepted, LogOutcome.Rejected]);

        var result = await new GetRunHandler(reads, lines).Handle(new GetRunQuery(run.Id), CancellationToken.None);

        result.Value.Run.Sent.Should().Be(3);
        result.Value.Outcomes.Should().BeEquivalentTo([new OutcomeCount(LogOutcome.Accepted, 2), new OutcomeCount(LogOutcome.Rejected, 1)]);
        result.Value.Actors.Should().BeEquivalentTo(run.Plan.Bookings.Select(booking => booking.PersonaName).Distinct());
    }

    [Fact]
    public async Task Open_NoSuchRun_IsNotFound()
    {
        var result = await new GetRunHandler(Substitute.For<IReadRepository<SimulationRun>>(), Substitute.For<IReadRepository<LogEntry>>())
            .Handle(new GetRunQuery(SimulationRunId.New()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ListLog_MapsTheLinesWithTheirKeys()
    {
        var run = TestData.Run();
        var lines = Substitute.For<IReadRepository<LogEntry>>();
        lines.ListAsync(Arg.Any<ISpecification<LogEntry, LogLine>>(), Arg.Any<CancellationToken>())
            .Returns([new LogLine(2, TestData.Now, "Brouwer Dranken", "Send booking", LogOutcome.Accepted, "c1", "SIM-1-1", "TO-1", "Good booking")]);

        var log = await new ListLogHandler(lines).Handle(new ListLogQuery(run.Id, new LogFilter()), CancellationToken.None);

        log.Should().ContainSingle().Which.Should().Be(new LogLine(2, TestData.Now, "Brouwer Dranken", "Send booking", LogOutcome.Accepted, "c1", "SIM-1-1", "TO-1", "Good booking"));
    }
}
