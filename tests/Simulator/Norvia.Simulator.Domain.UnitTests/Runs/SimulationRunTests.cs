using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.UnitTests.Plans;

namespace Norvia.Simulator.Domain.UnitTests.Runs;

public sealed class SimulationRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static SimulationRun Started()
    {
        var settings = TestWorld.Settings(orders: 5, badShare: 0);
        return SimulationRun.Start(4, settings, BookingPlanGenerator.Generate(settings, TestWorld.Benelux, 4), Now, "ngruson");
    }

    [Fact]
    public void Start_NewRun_IsRunningWithItsPlanAndNumber()
    {
        var run = Started();

        run.Status.Should().Be(SimulationRunStatus.Running);
        run.Number.Should().Be(4);
        run.Plan.Bookings.Should().HaveCount(5);
        run.StartedAt.Should().Be(Now);
        run.FinishedAt.Should().BeNull();
        run.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Complete_RunningRun_IsCompleted()
    {
        var run = Started();

        run.Complete(Now.AddMinutes(1));

        run.Status.Should().Be(SimulationRunStatus.Completed);
        run.FinishedAt.Should().Be(Now.AddMinutes(1));
        run.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Cancel_RunningRun_IsCancelled()
    {
        var run = Started();

        run.Cancel(Now.AddSeconds(5)).Should().BeTrue();

        run.Status.Should().Be(SimulationRunStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FinishedRun_ChangesNothing()
    {
        var run = Started();
        run.Complete(Now.AddMinutes(1));

        run.Cancel(Now.AddMinutes(2)).Should().BeFalse();

        run.Status.Should().Be(SimulationRunStatus.Completed);
        run.FinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Complete_CancelledRun_StaysCancelled()
    {
        var run = Started();
        run.Cancel(Now.AddSeconds(5));

        run.Complete(Now.AddMinutes(1));

        run.Status.Should().Be(SimulationRunStatus.Cancelled);
    }

    [Fact]
    public void Fail_RunningRun_IsFailed()
    {
        var run = Started();

        run.Fail(Now.AddSeconds(1));

        run.Status.Should().Be(SimulationRunStatus.Failed);
    }

    [Fact]
    public void ActiveRunSpec_SelectsOnlyRunsStillRunning()
    {
        var running = Started();
        var done = Started();
        done.Complete(Now);

        new ActiveRunSpec().Evaluate([running, done]).Should().ContainSingle().Which.Should().BeSameAs(running);
    }

    [Fact]
    public void RunsNewestFirstSpec_OrdersByStartTime()
    {
        var older = Started();
        var settings = TestWorld.Settings(orders: 1, badShare: 0);
        var newer = SimulationRun.Start(5, settings, BookingPlanGenerator.Generate(settings, TestWorld.Benelux, 5), Now.AddHours(1), "ngruson");

        new RunsNewestFirstSpec().Evaluate([older, newer]).Should().Equal(newer, older);
    }

    [Fact]
    public void RunSequence_Take_GivesNumbersOneAfterAnother()
    {
        var sequence = RunSequence.Create();

        sequence.Next.Should().Be(1);
        sequence.Take().Should().Be(1);
        sequence.Take().Should().Be(2);
        sequence.Next.Should().Be(3);
    }

    [Fact]
    public void Start_KeepsTheListColumnsBesideThePlan()
    {
        var run = Started();

        (run.Seed, run.PresetName, run.Mode, run.PlannedCount).Should().Be((7, null, RunMode.Burst, 5));
    }

    [Fact]
    public void RunSummariesSpec_ReadsTheListColumnsNewestFirst()
    {
        var older = Started();
        var settings = TestWorld.Settings(orders: 1, badShare: 0);
        var newer = SimulationRun.Start(5, settings, BookingPlanGenerator.Generate(settings, TestWorld.Benelux, 5), Now.AddHours(1), "ngruson");

        var rows = new RunSummariesSpec().Evaluate([older, newer]).ToList();

        rows.Select(row => row.Number).Should().Equal(5, 4);
        rows[1].Should().Be(new RunListRow(older.Id.Value, 4, SimulationRunStatus.Running, Now, null, "ngruson", 7, null, RunMode.Burst, 5));
    }

    [Fact]
    public void RunSequence_NextNumber_IsOneBeforeAnyRunIsNumbered()
    {
        RunSequence.NextNumber(null).Should().Be(1);
        var sequence = RunSequence.Create();
        sequence.Take();
        RunSequence.NextNumber(sequence).Should().Be(2);
    }

    [Theory]
    [InlineData(RunMode.Burst, 10, 4, 0)]
    [InlineData(RunMode.Paced, 8, 4, 120)]
    [InlineData(RunMode.Paced, 8, 0, 0)]
    public void Spacing_ABurstHasNone_APacedRunSpreadsTheWindowEvenly(RunMode mode, int windowMinutes, int bookings, int seconds)
    {
        var settings = new SimulationSettings(bookings, 0, 0, 1, mode, TimeSpan.FromMinutes(windowMinutes));

        settings.Spacing(bookings).Should().Be(TimeSpan.FromSeconds(seconds));
    }
}
