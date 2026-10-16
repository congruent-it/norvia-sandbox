using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Reset;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Reset;

public sealed class ResetEstateTests
{
    private readonly IRepository<SimulationRun> _runs = Substitute.For<IRepository<SimulationRun>>();
    private readonly IRepository<LogEntry> _log = Substitute.For<IRepository<LogEntry>>();
    private readonly IRepository<RunSequence> _sequences = Substitute.For<IRepository<RunSequence>>();
    private readonly IEstateReset _doors = Substitute.For<IEstateReset>();

    public ResetEstateTests()
    {
        _doors.CheckAsync(Arg.Any<EstateApp>(), Arg.Any<CancellationToken>())
            .Returns(call => new AppReadiness(call.Arg<EstateApp>(), Reachable: true, Enabled: true, BusySubscription: null));
        _doors.ResetAsync(Arg.Any<EstateApp>(), Arg.Any<CancellationToken>())
            .Returns(call => AppResetResult.Done(call.Arg<EstateApp>(), [new DeletedKind("Things", 3)]));
    }

    private ResetEstateHandler Handler() => new(_runs, _log, _sequences, _doors);

    private void GivenHistory(out List<SimulationRun> finished, out List<LogEntry> lines, out List<RunSequence> counter)
    {
        finished = [TestData.Run(), TestData.Run()];
        lines = [LogEntry.Note(finished[0].Id, 1, TestData.Now, "x")];
        counter = [RunSequence.Create()];
        var (f, l, c) = (finished, lines, counter);
        _runs.ListAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(f);
        _log.ListAsync(Arg.Any<ISpecification<LogEntry>>(), Arg.Any<CancellationToken>()).Returns(l);
        _sequences.ListAsync(Arg.Any<ISpecification<RunSequence>>(), Arg.Any<CancellationToken>()).Returns(c);
    }

    [Fact]
    public async Task Reset_AQuietEstate_ResetsEachApplicationThenClearsTheSimulatorLast()
    {
        GivenHistory(out var finished, out var lines, out var counter);

        var result = await Handler().Handle(new ResetEstateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Complete.Should().BeTrue();
        result.Value.Apps.Select(app => app.App).Should().Equal(EstateApp.Tms, EstateApp.PartnerPortal, EstateApp.DriverApp);
        result.Value.Simulator.Should().Equal(new DeletedKind("Runs", 2), new DeletedKind("Log lines", 1));
        await _runs.Received(1).DeleteRangeAsync(finished, Arg.Any<CancellationToken>());
        await _log.Received(1).DeleteRangeAsync(lines, Arg.Any<CancellationToken>());
        await _sequences.Received(1).DeleteRangeAsync(counter, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_ARunActive_TouchesNothing()
    {
        _runs.AnyAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().Handle(new ResetEstateCommand(), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Should().Contain("A run is active");
        await _doors.DidNotReceive().ResetAsync(Arg.Any<EstateApp>(), Arg.Any<CancellationToken>());
        await _runs.DidNotReceive().DeleteRangeAsync(Arg.Any<IEnumerable<SimulationRun>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_ASubscriptionHoldsMessages_TouchesNothingAndNamesIt()
    {
        _doors.CheckAsync(EstateApp.DriverApp, Arg.Any<CancellationToken>())
            .Returns(new AppReadiness(EstateApp.DriverApp, Reachable: true, Enabled: true, "driver-app-events/driver-reports"));

        var result = await Handler().Handle(new ResetEstateCommand(), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Should().Contain("driver-app-events/driver-reports");
        await _doors.DidNotReceive().ResetAsync(Arg.Any<EstateApp>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_AnApplicationRefuses_ResetsTheRestAndKeepsTheSimulatorsHistory()
    {
        _doors.ResetAsync(EstateApp.PartnerPortal, Arg.Any<CancellationToken>()).Returns(AppResetResult.Refused(EstateApp.PartnerPortal, "It answered 500."));

        var result = await Handler().Handle(new ResetEstateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Complete.Should().BeFalse();
        result.Value.Apps.Single(app => !app.Succeeded).Refusal.Should().Be("It answered 500.");
        result.Value.Simulator.Should().BeEmpty();
        await _doors.Received(1).ResetAsync(EstateApp.DriverApp, Arg.Any<CancellationToken>());
        await _runs.DidNotReceive().DeleteRangeAsync(Arg.Any<IEnumerable<SimulationRun>>(), Arg.Any<CancellationToken>());
        await _log.DidNotReceive().DeleteRangeAsync(Arg.Any<IEnumerable<LogEntry>>(), Arg.Any<CancellationToken>());
        await _sequences.DidNotReceive().DeleteRangeAsync(Arg.Any<IEnumerable<RunSequence>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReadiness_ReportsTheRunAndEachApplication()
    {
        var reads = Substitute.For<IReadRepository<SimulationRun>>();
        reads.AnyAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(true);

        var readiness = await new GetResetReadinessHandler(reads, _doors).Handle(new GetResetReadinessQuery(), CancellationToken.None);

        readiness.RunActive.Should().BeTrue();
        readiness.Apps.Should().HaveCount(3);
    }
}

public sealed class EstateResetReadinessTests
{
    private static AppReadiness Ready(EstateApp app) => new(app, Reachable: true, Enabled: true, BusySubscription: null);

    [Fact]
    public void Reason_EverythingQuiet_IsNone() =>
        new EstateResetReadiness(false, EstateApps.All.Select(Ready).ToList()).Reason.Should().BeNull();

    [Fact]
    public void Reason_ARunIsActive_ComesBeforeAnyApplication() =>
        new EstateResetReadiness(true, [new AppReadiness(EstateApp.Tms, false, false, null)]).Reason.Should().StartWith("A run is active");

    [Fact]
    public void Reason_AnApplicationCouldNotBeAsked_NamesIt() =>
        new EstateResetReadiness(false, [new AppReadiness(EstateApp.PartnerPortal, false, false, null)]).Reason
            .Should().Be("The Partner portal couldn't be asked whether it would take a reset.");

    [Fact]
    public void Reason_TheSettingIsOff_NamesTheApplication() =>
        new EstateResetReadiness(false, [Ready(EstateApp.Tms), new AppReadiness(EstateApp.DriverApp, true, false, null)]).Reason
            .Should().Be("The reset is switched off for the Driver app.");

    [Fact]
    public void Reason_ASubscriptionIsBusy_NamesIt() =>
        new EstateResetReadiness(false, [new AppReadiness(EstateApp.Tms, true, true, "trip-events/trip-dispatch")]).Reason
            .Should().Contain("trip-events/trip-dispatch");

    [Fact]
    public void NameOf_AnUnknownApplication_Throws()
    {
        var act = () => EstateApps.NameOf((EstateApp)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
