using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Infrastructure.Fleet;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

/// <summary>
/// Seeding the demo's day of Shipments to put on Trips (P11): it books each Own fleet Transport Order, assigns it to Own fleet and releases its
/// Shipment, only on a TMS that holds no Trip, and tries again when the database isn't ready. The mediator and the repository are substituted; time is faked.
/// </summary>
public sealed class DemoTripDaySeederTests : IAsyncDisposable
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
    private readonly DemoTripDay _demo;
    private readonly DemoTripDaySeeder _seeder;
    private int _numbers;

    public DemoTripDaySeederTests()
    {
        var options = Options.Create(new SeedOptions { Path = Path.Combine(AppContext.BaseDirectory, "DemoSeed", "norvia.seed.json") });
        _seed = DemoSeed.Read(options);
        _demo = _seed.TripDay!;
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _services = new ServiceCollection().AddSingleton(_mediator).AddSingleton(_trips).BuildServiceProvider();
        _mediator.Send(Arg.Any<SubmitTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success(new SubmitTransportOrderResult(Guid.NewGuid(), TransportOrderStatus.Accepted, $"TO-{++_numbers:D7}", [])));
        _mediator.Send(Arg.Any<AssignTransportOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new AssignTransportOrderResult(
                call.Arg<AssignTransportOrderCommand>().TransportOrderNumber.Replace("TO-", "SH-", StringComparison.Ordinal), call.Arg<AssignTransportOrderCommand>().TransportOrderNumber,
                Norvia.Tms.Domain.Shipments.ShipmentStatus.Planned,
                new Norvia.Tms.Application.Shipments.CarriedByView(Norvia.Tms.Application.Shipments.AssignedTo.OwnFleet, null), "SHP-0001", "corr")));
        _mediator.Send(Arg.Any<ReleaseShipmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new ReleaseShipmentResult(
                call.Arg<ReleaseShipmentCommand>().ShipmentNumber, "TO-0000001", Norvia.Tms.Domain.Shipments.ShipmentStatus.Released,
                new Norvia.Tms.Application.Shipments.CarriedByView(Norvia.Tms.Application.Shipments.AssignedTo.OwnFleet, null), "SHP-0001", "corr")));
        _seeder = SeederFor(_seed);
    }

    [Fact]
    public async Task StartAsync_BooksEachOrderForTheTripDay_AssignsItToOwnFleet_AndReleasesItsShipment()
    {
        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        var submitted = Received<SubmitTransportOrderCommand>();
        submitted.Should().HaveCount(_demo.TransportOrders.Count);
        submitted.Select(command => command.TransportOrder.ShipperReference).Should().OnlyHaveUniqueItems().And.OnlyContain(reference => reference!.EndsWith("20261008", StringComparison.Ordinal));
        Received<AssignTransportOrderCommand>().Should().OnlyContain(command => command.CarrierId == null, "the orders go to Own fleet");
        Received<ReleaseShipmentCommand>().Select(command => command.ShipmentNumber).Should().Equal(Received<AssignTransportOrderCommand>().Select(command => command.TransportOrderNumber.Replace("TO-", "SH-", StringComparison.Ordinal)));
        Received<ReleaseShipmentCommand>().Select(command => command.ReleaseKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task TheSeedFile_HasTwoTripsAndAShipmentTooHeavyForTheVan()
    {
        var weights = _demo.TransportOrders.ToDictionary(order => order.ShipperReference, order => (order.Pallets, Kg: order.Pallets * order.WeightKgPerPallet));

        var truck = weights.Where(entry => entry.Key is "DEMO-TRIP-1" or "DEMO-TRIP-2" or "DEMO-TRIP-3").Select(entry => entry.Value).ToList();
        var van = weights.Where(entry => entry.Key is "DEMO-TRIP-4" or "DEMO-TRIP-5").Select(entry => entry.Value).ToList();
        truck.Sum(item => item.Pallets).Should().BeLessThanOrEqualTo(18);
        truck.Sum(item => item.Kg).Should().BeLessThanOrEqualTo(10_000);
        truck.Sum(item => item.Pallets).Should().BeGreaterThan(4, "too many pallets for a van");
        van.Sum(item => item.Pallets).Should().BeLessThanOrEqualTo(4);
        van.Sum(item => item.Kg).Should().BeLessThanOrEqualTo(1_200);
        (van.Sum(item => item.Kg) + weights["DEMO-TRIP-HEAVY"].Kg).Should().BeGreaterThan(1_200, "adding the heavy one to the van's Trip breaks PayloadExceeded");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StartAsync_OnTheDayBeforeTheOverCapacityDaysWeekend_KeepsTheTripDayOffTheOverCapacityDay()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.FromHours(2))); // a Friday: the Over capacity day is Monday the 12th, and so would be the Trip day

        await _seeder.StartAsync(_cancellationToken);
        await SeededAsync();

        Received<SubmitTransportOrderCommand>().Should().OnlyContain(command => command.TransportOrder.ShipperReference!.EndsWith("20261013", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenATripExists_SeedsNothing()
    {
        _trips.AnyAsync(Arg.Any<TripsInOrderSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _seeder.StartAsync(_cancellationToken);
        await LogAsync(log => log.Message.Contains("already holds", StringComparison.Ordinal));

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheSeedHasNoTripDay_SeedsNothing()
    {
        using var seeder = SeederFor(new DemoSeed(_seed.OverCapacityDay));

        await seeder.StartAsync(_cancellationToken);
        await seeder.ExecuteTask!;

        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenTheDatabaseIsNotReady_TriesAgainAtTheNextLook()
    {
        var attempts = 0;
        _trips.AnyAsync(Arg.Any<TripsInOrderSpec>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1 ? Task.FromException<bool>(new InvalidOperationException("database down")) : Task.FromResult(false));

        await _seeder.StartAsync(_cancellationToken);
        (await LogAsync(log => log.Level == LogLevel.Warning)).Message.Should().Contain("trying again");
        _time.Advance(DemoTripDaySeeder.RetryInterval);

        await SeededAsync();
        Received<ReleaseShipmentCommand>().Should().HaveCount(_demo.TransportOrders.Count);
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
    public async Task StartAsync_WhenAReleaseIsRefused_Fails()
    {
        _mediator.Send(Arg.Any<ReleaseShipmentCommand>(), Arg.Any<CancellationToken>()).Returns(Result<ReleaseShipmentResult>.Invalid(new ValidationError("no")));

        await _seeder.StartAsync(_cancellationToken);
        var failed = await LogAsync(log => log.Level == LogLevel.Warning);

        failed.Exception!.Message.Should().Contain("failed at SH-");
    }

    public async ValueTask DisposeAsync()
    {
        await _seeder.StopAsync(CancellationToken.None);
        _seeder.Dispose();
        await _services.DisposeAsync();
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private DemoTripDaySeeder SeederFor(DemoSeed seed) => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        seed,
        new PlanningHorizonRules(14, Amsterdam),
        _time,
        _loggerFactory.CreateLogger<DemoTripDaySeeder>());

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
