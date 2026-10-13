using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// Seeds the demo's day of Shipments to put on Trips (P11) when the TMS first starts: it books the day's Own fleet Transport Orders, assigns
/// them to Own fleet and releases their Shipments, through the same commands as the Planner and the Partner API. A TMS that already holds
/// a Trip the Planner built seeds nothing (the Trip of P12's demo is not one), and a run that fails part-way is tried again, as booking, assigning and releasing the same orders again changes
/// nothing. The day is kept off the Over capacity day (P10), which would otherwise be the same day when the weekend gets in the way. It retries
/// every <see cref="RetryInterval"/> until the database takes it, as the TMS may start before its migrations are applied.
/// </summary>
internal sealed partial class DemoTripDaySeeder(
    IServiceScopeFactory scopes,
    DemoSeed seed,
    PlanningHorizonRules horizon,
    TimeProvider time,
    ILogger<DemoTripDaySeeder> logger) : BackgroundService
{
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (seed.TripDay is not { } demo)
        {
            return;
        }

        using var timer = new PeriodicTimer(RetryInterval, time);
        do
        {
            try
            {
                await SeedAsync(demo, stoppingToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SeedAsync(DemoTripDay demo, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        if ((await services.GetRequiredService<IReadRepository<Trip>>().ListAsync(new TripsInOrderSpec(), cancellationToken)).Any(trip => !DemoTripExecutionSeeder.IsDemoTrip(trip)))
        {
            LogAlreadySeeded();
            return;
        }

        var mediator = services.GetRequiredService<IMediator>();
        var start = horizon.Today(time.GetUtcNow());
        var day = demo.DayFor(start);
        if (seed.OverCapacityDay is { } overCapacity && overCapacity.DayFor(start) == day)
        {
            day = DemoBookings.WeekdayOnOrAfter(day.AddDays(1));
        }

        var shipments = new List<string>();
        foreach (var order in demo.TransportOrders)
        {
            var booking = DemoBookings.For(demo.ShipperId, demo.Collection, day, order, horizon.Zone);
            var submitted = Succeeded(await mediator.Send(
                new SubmitTransportOrderCommand(booking, IntakeChannel.ShipperApi, CorrelationId.New().Value), cancellationToken), order.ShipperReference);
            if (submitted.TransportOrderNumber is not { } number)
            {
                throw new InvalidOperationException($"The TMS rejected the demo booking {order.ShipperReference}.");
            }

            shipments.Add(Succeeded(await mediator.Send(
                new AssignTransportOrderCommand(number, CarrierId: null, $"demo-trip-{order.ShipperReference}-{day:yyyyMMdd}", CorrelationId.New().Value), cancellationToken), number).ShipmentNumber);
        }

        // Released last, so a Shipment is on the list only once all of them are planned.
        foreach (var shipment in shipments)
        {
            Succeeded(await mediator.Send(
                new ReleaseShipmentCommand(shipment, $"demo-trip-release-{shipment}-{day:yyyyMMdd}", CorrelationId.New().Value), cancellationToken), shipment);
        }

        LogSeeded(day, shipments.Count);
    }

    private static T Succeeded<T>(Result<T> result, string what) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Seeding the demo's Trip day failed at {what}: {result.Status}.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the demo's Trip day {Day:yyyy-MM-dd} with {ShipmentCount} Released Own fleet Shipments")]
    private partial void LogSeeded(DateOnly day, int shipmentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS already holds a Trip, so the demo's Trip day isn't seeded")]
    private partial void LogAlreadySeeded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding the demo's Trip day failed; trying again at the next look")]
    private partial void LogFailed(Exception exception);
}
