using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Fleet.Planning.RecordAbsence;
using Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Fleet;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

/// <summary>
/// Seeding the demo's Over capacity day (P10): it books and assigns the Own fleet Transport Orders, then records the Off the
/// road day and the Absence, only on a TMS that holds none of either, and tries again when the database isn't ready. The
/// mediator and the repositories are substituted; time is faked.
/// </summary>
public sealed class DemoOverCapacityDaySeederTests : IAsyncDisposable
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IReadRepository<Absence> _absences = Substitute.For<IReadRepository<Absence>>();
    private readonly IReadRepository<OffTheRoadDay> _offTheRoadDays = Substitute.For<IReadRepository<OffTheRoadDay>>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(2))); // a Monday
    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServiceProvider _services;
    private readonly DemoOverCapacityDay _demo;
    private readonly DemoOverCapacityDaySeeder _seeder;
    private int _numbers;

    public DemoOverCapacityDaySeederTests()
    {
        var options = Options.Create(new SeedOptions { Path = Path.Combine(AppContext.BaseDirectory, "DemoSeed", "norvia.seed.json") });
        _demo = DemoSeed.Read(options).OverCapacityDay!;
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _services = new ServiceCollection().AddSingleton(_mediator).AddSingleton(_absences).AddSingleton(_offTheRoadDays).BuildServiceProvider();
        SubmitAccepts();
        AssignSucceeds();
        _mediator.Send(Arg.Any<RecordOffTheRoadCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new RecordOffTheRoadResult(default, default, 1)));
        _mediator.Send(Arg.Any<RecordAbsenceCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new RecordAbsenceResult(default, default, 1)));
        _seeder = SeederFor(new DemoSeed(_demo));
    }

    [Fact]
    public async Task StartAsync_OnATmsWithNoAbsencesOrOffTheRoadDays_BooksAndAssignsTheOrdersToOwnFleet_ThenRecordsTheOffTheRoadDayAndTheAbsence()
    {
        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        var day = new DateOnly(2026, 10, 7); // two days after the Monday it starts
        var submitted = Received<SubmitTransportOrderCommand>();
        submitted.Should().HaveCount(_demo.TransportOrders.Count);
        submitted.Should().OnlyContain(command => command.Channel == IntakeChannel.ShipperApi);
        submitted.Select(command => command.TransportOrder.ShipperReference).Should().OnlyHaveUniqueItems().And.OnlyContain(reference => reference!.EndsWith("20261007", StringComparison.Ordinal));
        var assigned = Received<AssignTransportOrderCommand>();
        assigned.Should().OnlyContain(command => command.CarrierId == null, "the orders go to Own fleet");
        assigned.Select(command => command.AssignmentKey).Should().OnlyHaveUniqueItems();
        Received<RecordOffTheRoadCommand>().Should().ContainSingle().Which.Should().Match<RecordOffTheRoadCommand>(command =>
            command.VehicleId == _demo.VehicleId && command.From == day && command.To == day && command.Reason == OffTheRoadReason.Maintenance);
        Received<RecordAbsenceCommand>().Should().ContainSingle().Which.Should().Match<RecordAbsenceCommand>(command =>
            command.DriverId == _demo.DriverId && command.From == day && command.To == day && command.Reason == AbsenceReason.Leave);
    }

    [Fact]
    public async Task StartAsync_RecordsTheOffTheRoadDayAndTheAbsenceLast_SoAnInterruptedSeedIsTriedAgain()
    {
        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        var commands = _mediator.ReceivedCalls().SelectMany(call => call.GetArguments()).Where(argument => argument is not CancellationToken).Select(argument => argument!.GetType()).ToList();
        commands.Take(commands.Count - 2).Should().OnlyContain(type => type == typeof(SubmitTransportOrderCommand) || type == typeof(AssignTransportOrderCommand));
        commands.TakeLast(2).Should().BeEquivalentTo([typeof(RecordOffTheRoadCommand), typeof(RecordAbsenceCommand)]);
    }

    [Fact]
    public async Task StartAsync_OnAWeekendStart_PutsTheDayOnTheNextWeekday()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.FromHours(2))); // a Saturday

        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        Received<RecordOffTheRoadCommand>().Single().From.Should().Be(new DateOnly(2026, 10, 12));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartAsync_WhenAnAbsenceOrAnOffTheRoadDayIsRecorded_SeedsNothing(bool absence)
    {
        if (absence)
        {
            _absences.AnyAsync(Arg.Any<AbsencesFromSpec>(), Arg.Any<CancellationToken>()).Returns(true);
        }
        else
        {
            _offTheRoadDays.AnyAsync(Arg.Any<OffTheRoadDaysFromSpec>(), Arg.Any<CancellationToken>()).Returns(true);
        }

        await _seeder.StartAsync(_cancellationToken);
        await LogAsync(log => log.Message.Contains("already holds", StringComparison.Ordinal));

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheSeedHasNoOverCapacityDay_SeedsNothing()
    {
        using var seeder = SeederFor(new DemoSeed(null));

        await seeder.StartAsync(_cancellationToken);
        await seeder.ExecuteTask!;

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheDatabaseIsNotReady_TriesAgainAtTheNextLook()
    {
        var attempts = 0;
        _absences.AnyAsync(Arg.Any<AbsencesFromSpec>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1 ? Task.FromException<bool>(new InvalidOperationException("database down")) : Task.FromResult(false));

        await _seeder.StartAsync(_cancellationToken);
        var failed = await LogAsync(log => log.Level == LogLevel.Warning);
        failed.Message.Should().Contain("trying again");
        _time.Advance(DemoOverCapacityDaySeeder.RetryInterval);

        await SeededAsync();
        Received<SubmitTransportOrderCommand>().Should().HaveCount(_demo.TransportOrders.Count);
    }

    [Fact]
    public async Task StartAsync_WhenTheTmsRejectsABooking_FailsWithoutRecordingTheOffTheRoadDay()
    {
        _mediator.Send(Arg.Any<SubmitTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SubmitTransportOrderResult(Guid.NewGuid(), TransportOrderStatus.Rejected, null, [])));

        await _seeder.StartAsync(_cancellationToken);
        var failed = await LogAsync(log => log.Level == LogLevel.Warning);

        failed.Exception!.Message.Should().Contain("rejected the demo booking");
        Received<RecordOffTheRoadCommand>().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheAssignmentIsRefused_FailsWithoutRecordingTheOffTheRoadDay()
    {
        _mediator.Send(Arg.Any<AssignTransportOrderCommand>(), Arg.Any<CancellationToken>()).Returns(Result<AssignTransportOrderResult>.Invalid(new ValidationError("no")));

        await _seeder.StartAsync(_cancellationToken);
        var failed = await LogAsync(log => log.Level == LogLevel.Warning);

        failed.Exception!.Message.Should().Contain("failed at TO-");
        Received<RecordOffTheRoadCommand>().Should().BeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        await _seeder.StopAsync(CancellationToken.None);
        _seeder.Dispose();
        await _services.DisposeAsync();
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private DemoOverCapacityDaySeeder SeederFor(DemoSeed seed) => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        seed,
        new PlanningHorizonRules(14, Amsterdam),
        _time,
        _loggerFactory.CreateLogger<DemoOverCapacityDaySeeder>());

    private void SubmitAccepts() =>
        _mediator.Send(Arg.Any<SubmitTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success(new SubmitTransportOrderResult(Guid.NewGuid(), TransportOrderStatus.Accepted, $"TO-{++_numbers:D7}", [])));

    private void AssignSucceeds() =>
        _mediator.Send(Arg.Any<AssignTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new AssignTransportOrderResult(
                "SH-0000001", call.Arg<AssignTransportOrderCommand>().TransportOrderNumber, Norvia.Tms.Domain.Shipments.ShipmentStatus.Planned,
                new Norvia.Tms.Application.Shipments.CarriedByView(Norvia.Tms.Application.Shipments.AssignedTo.OwnFleet, null), "SHP-0001", "corr")));

    private List<T> Received<T>() => [.. _mediator.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<T>()];

    private async Task SeededAsync() => await LogAsync(log => log.Level == LogLevel.Information && log.Message.StartsWith("Seeded the demo", StringComparison.Ordinal));

    private async Task<FakeLogRecord> LogAsync(Func<FakeLogRecord, bool> match)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (_logs.Collector.GetSnapshot().FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }
}
