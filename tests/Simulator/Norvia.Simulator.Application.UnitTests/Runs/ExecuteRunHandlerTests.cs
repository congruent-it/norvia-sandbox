using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Runs;

public sealed class ExecuteRunHandlerTests
{
    private readonly IRepository<SimulationRun> _runs = Substitute.For<IRepository<SimulationRun>>();
    private readonly IRepository<LogEntry> _log = Substitute.For<IRepository<LogEntry>>();
    private readonly IPartnerApi _api = Substitute.For<IPartnerApi>();
    private readonly ITmsApi _tms = Substitute.For<ITmsApi>();
    private readonly IDriverApp _drivers = Substitute.For<IDriverApp>();
    private readonly IWorldSource _world = Substitute.For<IWorldSource>();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly List<LogEntry> _lines = [];

    public ExecuteRunHandlerTests()
    {
        _world.Load().Returns(TestData.World);
        _log.AddAsync(Arg.Do<LogEntry>(line => { lock (_lines) { _lines.Add(line); } }), Arg.Any<CancellationToken>()).Returns(call => call.Arg<LogEntry>());
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(call => new BookingAnswer(LogOutcome.Accepted, $"corr-{call.Arg<PlannedBooking>().Step}", $"TO-{call.Arg<PlannedBooking>().Step:0000000}", null));
    }

    private ExecuteRunHandler Handler(int concurrency = 3) =>
        new(_runs, _log, _api, _tms, _world, new SimulationLimits(50, concurrency), _time, new CarrierPacing(TimeSpan.Zero, 1), _drivers, new DriverPacing(TimeSpan.Zero, 1), Substitute.For<IErpApi>(), Substitute.For<IShipperInbox>(), Substitute.For<ICarrierInbox>());

    private SimulationRun Active(SimulationSettings settings)
    {
        var run = TestData.Run(settings);
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        return run;
    }

    [Fact]
    public async Task Handle_Burst_SendsEveryBookingAsItsPersonaAndLogsTheAnswer()
    {
        var run = Active(TestData.Settings(orders: 6));

        var result = await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        foreach (var booking in run.Plan.Bookings)
        {
            await _api.Received(1).SendBookingAsync(booking, Arg.Any<CancellationToken>());
        }

        var bookings = _lines.Where(line => line.Outcome == LogOutcome.Accepted).OrderBy(line => line.ShipperReference).ToList();
        bookings.Should().HaveCount(6);
        var first = bookings.Single(line => line.ShipperReference == "SIM-1-1");
        first.Actor.Should().Be(run.Plan.Bookings[0].PersonaName);
        first.Action.Should().Be("Send booking");
        first.CorrelationId.Should().Be("corr-1");
        first.TransportOrderNumber.Should().Be("TO-0000001");
        first.At.Should().Be(TestData.Now);
    }

    [Fact]
    public async Task Handle_Finished_CompletesTheRunAndClosesTheLog()
    {
        var run = Active(TestData.Settings(orders: 3));

        await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        await _runs.Received().UpdateAsync(run, Arg.Any<CancellationToken>());
        _lines.Last().Outcome.Should().Be(LogOutcome.Info);
        _lines.Last().Action.Should().Contain("finished").And.Contain("3 bookings");
    }

    [Fact]
    public async Task Handle_Lines_AreNumberedFromTwoAfterTheRunStarted()
    {
        var run = Active(TestData.Settings(orders: 4));

        await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None);

        _lines.Select(line => line.Sequence).Order().Should().Equal(Enumerable.Range(2, 5));
    }

    [Fact]
    public async Task Handle_ABadBooking_LogsWhatWasIntendedAndWhatNorviaSaid()
    {
        var run = Active(TestData.Settings(orders: 10, bad: 100, seed: 2));
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(new BookingAnswer(LogOutcome.Rejected, "corr-x", null, "ShipperReferenceReused"));

        await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None);

        var line = _lines.First(entry => entry.Outcome == LogOutcome.Rejected);
        line.Detail.Should().StartWith("Bad booking (").And.Contain("Norvia: ShipperReferenceReused");
        line.TransportOrderNumber.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NorviaNotReached_IsLoggedAsNotReceivedAndTheRunGoesOn()
    {
        var run = Active(TestData.Settings(orders: 3));
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(new BookingAnswer(LogOutcome.NotReceived, null, null, "Norvia couldn't be reached"));

        await Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None);

        _lines.Count(line => line.Outcome == LogOutcome.NotReceived).Should().Be(3);
        run.Status.Should().Be(SimulationRunStatus.Completed);
    }

    [Fact]
    public async Task Handle_TheConcurrencyCap_LimitsRequestsInFlight()
    {
        var run = Active(TestData.Settings(orders: 12));
        var inFlight = 0;
        var highest = 0;
        var release = new TaskCompletionSource();
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref highest, now);
            await release.Task;
            Interlocked.Decrement(ref inFlight);
            return new BookingAnswer(LogOutcome.Accepted, "c", "TO-1", null);
        });

        var handling = Handler(concurrency: 2).Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None).AsTask();
        await WaitUntilAsync(() => Volatile.Read(ref inFlight) == 2);
        await Task.Yield();
        Volatile.Read(ref inFlight).Should().Be(2, "a third request waits for a free slot");
        release.SetResult();
        await handling;

        highest.Should().Be(2);
        _lines.Count(line => line.Outcome == LogOutcome.Accepted).Should().Be(12);
    }

    [Fact]
    public async Task Handle_Paced_SpreadsTheBookingsOverTheWindow()
    {
        var run = Active(TestData.Settings(orders: 4, mode: RunMode.Paced, windowMinutes: 8));

        var handling = Task.Run(() => Handler().Handle(new ExecuteRunCommand(run.Id, CancellationToken.None), CancellationToken.None).AsTask());

        await WaitUntilAsync(() => SentCount() == 1);
        for (var turn = 0; turn < 50; turn++)
        {
            await Task.Yield();
        }

        SentCount().Should().Be(1, "the second booking is due two minutes in");
        for (var expected = 2; expected <= 4; expected++)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            await WaitUntilAsync(() => SentCount() == expected);
        }

        (await handling).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Cancelled_SendsNoMoreAndSaysSo()
    {
        var run = Active(TestData.Settings(orders: 6));
        using var stop = new CancellationTokenSource();
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            return Task.FromResult(new BookingAnswer(LogOutcome.Accepted, "c", "TO-1", null));
        });

        var result = await Handler(concurrency: 1).Handle(new ExecuteRunCommand(run.Id, stop.Token), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _api.Received(1).SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>());
        _lines.Last().Action.Should().Contain("cancelled");
        run.Status.Should().Be(SimulationRunStatus.Running, "the cancel command, not the executor, ends a cancelled run");
    }

    [Fact]
    public async Task Handle_NoActiveRun_IsNotFound()
    {
        var result = await Handler().Handle(new ExecuteRunCommand(SimulationRunId.New(), CancellationToken.None), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _api.DidNotReceive().SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>());
    }

    private int SentCount() => _api.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPartnerApi.SendBookingAsync));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition never held.");
            }

            await Task.Yield();
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
        }
        while (value > seen && Interlocked.CompareExchange(ref target, value, seen) != seen);
    }
}
