using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.Dispatch;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// Seeds the Trip of P12's demo when the TMS first starts: it books the day's Own fleet Transport Orders, assigns them to Own fleet, releases
/// their Shipments, puts them on a Trip for the seeded Driver and Vehicle and Dispatches it, through the same commands as the Planner and the
/// Partner API. The Dispatch publishes <c>TripDispatched</c>, so the Trip reaches the Driver app as any Dispatched Trip does. A TMS that already
/// holds a Trip seeds nothing, and a run that fails part-way is tried again, as every step before the Dispatch changes nothing when repeated. It
/// retries every <see cref="RetryInterval"/> until the database takes it and the planning horizon has opened the day, as the TMS may start before either.
/// </summary>
internal sealed partial class DemoTripExecutionSeeder(
    IServiceScopeFactory scopes,
    DemoSeed seed,
    PlanningHorizonRules horizon,
    TimeProvider time,
    ILogger<DemoTripExecutionSeeder> logger) : BackgroundService
{
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>The start of the idempotency key of the demo's Trip, which tells it from a Trip the Planner built.</summary>
    internal const string TripKeyPrefix = "demo-trip-execution-";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (seed.TripExecution is not { } demo)
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

    private async Task SeedAsync(DemoTripExecution demo, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var trips = services.GetRequiredService<IReadRepository<Trip>>();
        var existing = await trips.ListAsync(new TripsInOrderSpec(), cancellationToken);
        if (existing.Any(trip => trip.Status != TripStatus.Draft || !IsDemoTrip(trip)))
        {
            LogAlreadySeeded();
            return;
        }

        var mediator = services.GetRequiredService<IMediator>();
        var day = demo.DayFor(horizon.Today(time.GetUtcNow()));

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
                new AssignTransportOrderCommand(number, CarrierId: null, $"demo-exec-{order.ShipperReference}-{day:yyyyMMdd}", CorrelationId.New().Value), cancellationToken), number).ShipmentNumber);
        }

        foreach (var shipment in shipments)
        {
            Succeeded(await mediator.Send(
                new ReleaseShipmentCommand(shipment, $"demo-exec-release-{shipment}-{day:yyyyMMdd}", CorrelationId.New().Value), cancellationToken), shipment);
        }

        var draft = Succeeded(await mediator.Send(
            new CreateDraftTripCommand(
                day, demo.Vehicle, demo.Driver, new IdempotencyKey($"{TripKeyPrefix}{day:yyyyMMdd}"), demo.Start, demo.DrivingDuration, shipments), cancellationToken), "the Trip");
        var dispatched = Succeeded(await mediator.Send(new DispatchTripCommand(draft.Number, CorrelationId.New().Value), cancellationToken), draft.Number);

        LogSeeded(dispatched.Number, day, shipments.Count);
    }

    /// <summary>Whether the Trip is the demo's, not one the Planner built.</summary>
    internal static bool IsDemoTrip(Trip trip) => trip.IdempotencyKey.Value.StartsWith(TripKeyPrefix, StringComparison.Ordinal);

    private static T Succeeded<T>(Result<T> result, string what) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Seeding the demo's Trip failed at {what}: {result.Status} {string.Join("; ", result.ValidationErrors.Select(error => error.ErrorMessage))}.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the demo's Trip {TripNumber} Dispatched for {Day:yyyy-MM-dd} with {ShipmentCount} Own fleet Shipments")]
    private partial void LogSeeded(string tripNumber, DateOnly day, int shipmentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS already holds a Trip, so the demo's Trip isn't seeded")]
    private partial void LogAlreadySeeded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding the demo's Trip failed; trying again at the next look")]
    private partial void LogFailed(Exception exception);
}
