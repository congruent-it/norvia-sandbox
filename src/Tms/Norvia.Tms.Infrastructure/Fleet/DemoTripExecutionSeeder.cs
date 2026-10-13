using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// holds a Trip, other than a demo Draft whose Dispatch failed, seeds nothing, and a run that fails part-way is tried again, as every step before
/// the Dispatch changes nothing when repeated. Seeding is tried again until the planning horizon has opened the day.
/// </summary>
internal sealed partial class DemoTripExecutionSeeder(
    IServiceScopeFactory scopes,
    DemoSeed seed,
    PlanningHorizonRules horizon,
    TimeProvider time,
    ILogger<DemoTripExecutionSeeder> logger) : DemoSeeder(time)
{
    private const string KeyPrefix = "demo-exec";

    private readonly TimeProvider _time = time;

    protected override bool HasSeed => seed.TripExecution is not null;

    protected override async Task SeedAsync(CancellationToken cancellationToken)
    {
        var demo = seed.TripExecution!;
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var trips = await services.GetRequiredService<IReadRepository<Trip>>().ListAsync(new TripsInOrderSpec(), cancellationToken);
        if (trips.Any(trip => trip.Status != TripStatus.Draft || !DemoTrips.IsDemoTrip(trip)))
        {
            LogAlreadySeeded();
            return;
        }

        var mediator = services.GetRequiredService<IMediator>();
        var day = demo.DayFor(horizon.Today(_time.GetUtcNow()));

        var shipments = await DemoBookings.BookAndAssignAsync(mediator, demo.ShipperId, demo.Collection, day, demo.TransportOrders, horizon.Zone, KeyPrefix, cancellationToken);
        await DemoBookings.ReleaseAsync(mediator, shipments, day, KeyPrefix, cancellationToken);

        var draft = DemoBookings.Succeeded(await mediator.Send(
            new CreateDraftTripCommand(
                day, demo.Vehicle, demo.Driver, new IdempotencyKey($"{DemoTrips.KeyPrefix}{day:yyyyMMdd}"), demo.Start, demo.DrivingDuration, [.. shipments]), cancellationToken), "the Trip");
        var dispatched = DemoBookings.Succeeded(await mediator.Send(new DispatchTripCommand(draft.Number, CorrelationId.New().Value), cancellationToken), draft.Number);

        LogSeeded(dispatched.Number, day, shipments.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the demo's Trip {TripNumber} Dispatched for {Day:yyyy-MM-dd} with {ShipmentCount} Own fleet Shipments")]
    private partial void LogSeeded(string tripNumber, DateOnly day, int shipmentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS already holds a Trip (the demo's, or one the Planner built), so the demo's Trip isn't seeded")]
    private partial void LogAlreadySeeded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding the demo's Trip failed; trying again at the next look")]
    protected override partial void LogFailed(Exception exception);
}
