using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// Seeds the demo's day of Shipments to put on Trips (P11) when the TMS first starts: it books the day's Own fleet Transport Orders, assigns
/// them to Own fleet and releases their Shipments, through the same commands as the Planner and the Partner API. A TMS that already holds
/// a Trip the Planner built seeds nothing (<see cref="DemoTrips.IsDemoTrip"/> tells the Trip of P12's demo from one), and a run that fails part-way is tried again, as booking, assigning and releasing the same orders again changes
/// nothing. The day is kept off the Over capacity day (P10), which would otherwise be the same day when the weekend gets in the way.
/// </summary>
internal sealed partial class DemoTripDaySeeder(
    IServiceScopeFactory scopes,
    DemoSeed seed,
    PlanningHorizonRules horizon,
    TimeProvider time,
    ILogger<DemoTripDaySeeder> logger) : DemoSeeder(time)
{
    private const string KeyPrefix = "demo-trip";

    private readonly TimeProvider _time = time;

    protected override bool HasSeed => seed.TripDay is not null;

    protected override async Task SeedAsync(CancellationToken cancellationToken)
    {
        var demo = seed.TripDay!;
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var trips = await services.GetRequiredService<IReadRepository<Trip>>().ListAsync(new TripsInOrderSpec(), cancellationToken);
        if (trips.Any(trip => !DemoTrips.IsDemoTrip(trip)))
        {
            LogAlreadySeeded();
            return;
        }

        var mediator = services.GetRequiredService<IMediator>();
        var start = horizon.Today(_time.GetUtcNow());
        var day = demo.DayFor(start);
        if (seed.OverCapacityDay is { } overCapacity && overCapacity.DayFor(start) == day)
        {
            day = DemoBookings.WeekdayOnOrAfter(day.AddDays(1));
        }

        var shipments = await DemoBookings.BookAndAssignAsync(mediator, demo.ShipperId, demo.Collection, day, demo.TransportOrders, horizon.Zone, KeyPrefix, cancellationToken);
        await DemoBookings.ReleaseAsync(mediator, shipments, day, KeyPrefix, cancellationToken);

        LogSeeded(day, shipments.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the demo's Trip day {Day:yyyy-MM-dd} with {ShipmentCount} Released Own fleet Shipments")]
    private partial void LogSeeded(DateOnly day, int shipmentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS already holds a Trip the Planner built, so the demo's Trip day isn't seeded")]
    private partial void LogAlreadySeeded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding the demo's Trip day failed; trying again at the next look")]
    protected override partial void LogFailed(Exception exception);
}
