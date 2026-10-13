using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.Dispatch;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Infrastructure.Fleet;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

/// <summary>
/// Seeding the Trip of P12's demo: it books each Own fleet Transport Order for the day, assigns it, releases its Shipment, puts the Shipments on a Trip
/// for the seeded Driver and Vehicle and Dispatches it, only on a TMS that holds no Trip of the Planner's, and tries again when the database isn't ready.
/// The mediator and the repository are substituted; time is faked.
/// </summary>
public sealed class DemoTripExecutionSeederTests : IAsyncDisposable
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IReadRepository<Trip> _trips = Substitute.For<IReadRepository<Trip>>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(2))); // a Monday
    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServiceProvider _services;
    private readonly DemoSeed _seed;
    private readonly DemoTripExecution _demo;
    private readonly DemoTripExecutionSeeder _seeder;
    private int _numbers;

    public DemoTripExecutionSeederTests()
    {
        var options = Options.Create(new SeedOptions { Path = Path.Combine(AppContext.BaseDirectory, "DemoSeed", "norvia.seed.json") });
        _seed = DemoSeed.Read(options);
        _demo = _seed.TripExecution!;
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _services = new ServiceCollection().AddSingleton(_mediator).AddSingleton(_trips).BuildServiceProvider();
        _trips.ListAsync(Arg.Any<TripsInOrderSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _mediator.Send(Arg.Any<SubmitTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success(new SubmitTransportOrderResult(Guid.NewGuid(), TransportOrderStatus.Accepted, $"TO-{++_numbers:D7}", [])));
        _mediator.Send(Arg.Any<AssignTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new AssignTransportOrderResult(
                call.Arg<AssignTransportOrderCommand>().TransportOrderNumber.Replace("TO-", "SH-", StringComparison.Ordinal), call.Arg<AssignTransportOrderCommand>().TransportOrderNumber,
                ShipmentStatus.Planned, new CarriedByView(AssignedTo.OwnFleet, null), "SHP-0001", "corr")));
        _mediator.Send(Arg.Any<ReleaseShipmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new ReleaseShipmentResult(
                call.Arg<ReleaseShipmentCommand>().ShipmentNumber, "TO-0000001", ShipmentStatus.Released, new CarriedByView(AssignedTo.OwnFleet, null), "SHP-0001", "corr")));
        _mediator.Send(Arg.Any<CreateDraftTripCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new CreateDraftTripResult("TR-0000001")));
        _mediator.Send(Arg.Any<DispatchTripCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new DispatchTripResult("TR-0000001", 1, "corr")));
        _seeder = SeederFor(_seed);
    }

    [Fact]
    public async Task StartAsync_BooksAssignsAndReleasesEachOrder_ThenDispatchesATripForTheSeededDriverAndVehicleToday()
    {
        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        Received<SubmitTransportOrderCommand>().Select(command => command.TransportOrder.ShipperReference).Should()
            .HaveCount(3).And.OnlyHaveUniqueItems().And.OnlyContain(reference => reference!.EndsWith("20261005", StringComparison.Ordinal), "today");
        Received<AssignTransportOrderCommand>().Should().OnlyContain(command => command.CarrierId == null, "the orders go to Own fleet");
        var shipments = Received<ReleaseShipmentCommand>().Select(command => command.ShipmentNumber).ToList();
        shipments.Should().HaveCount(3);
        var draft = Received<CreateDraftTripCommand>().Should().ContainSingle().Subject;
        draft.Day.Should().Be(new DateOnly(2026, 10, 5));
        draft.DriverId.Should().Be(new DriverId("DRV-0003"));
        draft.VehicleId.Should().Be(new VehicleId("VEH-0003"));
        draft.PlannedStart.Should().Be(new TimeOnly(9, 0));
        draft.PlannedDrivingDuration.Should().Be(TimeSpan.FromHours(3));
        draft.ShipmentNumbers.Should().Equal(shipments);
        draft.IdempotencyKey.Value.Should().StartWith(DemoTrips.KeyPrefix);
        Received<DispatchTripCommand>().Should().ContainSingle().Which.Number.Should().Be("TR-0000001");
    }

    [Fact]
    public async Task StartAsync_OnASaturday_SeedsTheTripForMonday_SoTheDriverHasAShift()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.FromHours(2)));

        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        Received<CreateDraftTripCommand>().Single().Day.Should().Be(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public async Task StartAsync_WhenATripOfThePlannersExists_SeedsNothing()
    {
        _trips.ListAsync(Arg.Any<TripsInOrderSpec>(), Arg.Any<CancellationToken>()).Returns([ADraft("planner-key")]);

        await _seeder.StartAsync(_cancellationToken);
        await LogAsync(log => log.Message.Contains("already holds", StringComparison.Ordinal));

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheDemosTripIsStillADraft_DispatchesItAfterAllBookingsAreRepeated()
    {
        _trips.ListAsync(Arg.Any<TripsInOrderSpec>(), Arg.Any<CancellationToken>()).Returns([ADraft(DemoTrips.KeyPrefix + "20261005")]);

        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        Received<DispatchTripCommand>().Should().ContainSingle();
    }

    [Fact]
    public async Task StartAsync_WhenTheSeedHasNoTripExecution_SeedsNothing()
    {
        using var seeder = SeederFor(new DemoSeed(_seed.OverCapacityDay, _seed.TripDay));

        await seeder.StartAsync(_cancellationToken);
        await seeder.ExecuteTask!;

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheDatabaseIsNotReady_TriesAgainAtTheNextLook()
    {
        var attempts = 0;
        _trips.ListAsync(Arg.Any<TripsInOrderSpec>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1 ? Task.FromException<List<Trip>>(new InvalidOperationException("database down")) : Task.FromResult<List<Trip>>([]));

        await _seeder.StartAsync(_cancellationToken);
        (await LogAsync(log => log.Level == LogLevel.Warning)).Message.Should().Contain("trying again");
        _time.Advance(DemoSeeder.RetryInterval);

        await SeededAsync();
        Received<DispatchTripCommand>().Should().ContainSingle();
    }

    [Fact]
    public async Task StartAsync_WhenTheTripIsRefused_FailsWithoutDispatching()
    {
        _mediator.Send(Arg.Any<CreateDraftTripCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateDraftTripResult>.Invalid(new ValidationError("the Driver has no Shift that day")));

        await _seeder.StartAsync(_cancellationToken);
        var failed = await LogAsync(log => log.Level == LogLevel.Warning);

        failed.Exception!.Message.Should().Contain("the Trip").And.Contain("no Shift");
        Received<DispatchTripCommand>().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheTmsRejectsABooking_FailsWithoutReleasingAnything()
    {
        _mediator.Send(Arg.Any<SubmitTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SubmitTransportOrderResult(Guid.NewGuid(), TransportOrderStatus.Rejected, null, [])));

        await _seeder.StartAsync(_cancellationToken);
        var failed = await LogAsync(log => log.Level == LogLevel.Warning);

        failed.Exception!.Message.Should().Contain("rejected the demo booking");
        Received<ReleaseShipmentCommand>().Should().BeEmpty();
    }

    [Fact]
    public void TheSeedFile_HasThreeShipmentsThatFitTheVehicleAndTheDriversLicence()
    {
        _demo.TransportOrders.Should().HaveCount(3);
        _demo.TransportOrders.Sum(order => order.Pallets).Should().BeLessThanOrEqualTo(18, "the rigid truck's pallet places");
        _demo.TransportOrders.Sum(order => order.Pallets * order.WeightKgPerPallet).Should().BeLessThanOrEqualTo(10_000);
        _demo.TransportOrders.Select(order => order.ShipperReference).Should().OnlyHaveUniqueItems();
    }

    public async ValueTask DisposeAsync()
    {
        await _seeder.StopAsync(CancellationToken.None);
        _seeder.Dispose();
        await _services.DisposeAsync();
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private static Trip ADraft(string key) => Trip.CreateDraft(TripNumber.FromSequence(1), new DateOnly(2026, 10, 5), null, null, new IdempotencyKey(key));

    private DemoTripExecutionSeeder SeederFor(DemoSeed seed) => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        seed,
        new PlanningHorizonRules(14, Amsterdam),
        _time,
        _loggerFactory.CreateLogger<DemoTripExecutionSeeder>());

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
