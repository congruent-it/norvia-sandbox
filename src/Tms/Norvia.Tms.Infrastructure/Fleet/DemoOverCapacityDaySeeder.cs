using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.Fleet.Planning.RecordAbsence;
using Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// Seeds the demo's Over capacity day (P10) once, when the TMS first starts: it books the day's Own fleet Transport Orders and
/// assigns them to Own fleet, then records the Off the road day and the Absence. Those two come last and mark the seed as done:
/// a TMS that already holds any, whether seeded or recorded by the Planner, seeds nothing, and a run that fails part-way is
/// tried again, as booking and assigning the same orders again changes nothing. It goes through the same commands as the
/// Planner and the Partner API, so the day is Over capacity by the TMS's own load check. It retries every
/// <see cref="RetryInterval"/> until the database takes it, as the TMS may start before its migrations are applied.
/// </summary>
internal sealed partial class DemoOverCapacityDaySeeder(
    IServiceScopeFactory scopes,
    DemoSeed seed,
    PlanningHorizonRules horizon,
    TimeProvider time,
    ILogger<DemoOverCapacityDaySeeder> logger) : BackgroundService
{
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (seed.OverCapacityDay is not { } demo)
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

    private async Task SeedAsync(DemoOverCapacityDay demo, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var absences = services.GetRequiredService<IReadRepository<Absence>>();
        var offTheRoadDays = services.GetRequiredService<IReadRepository<OffTheRoadDay>>();
        if (await absences.AnyAsync(new AbsencesFromSpec(DateOnly.MinValue), cancellationToken)
            || await offTheRoadDays.AnyAsync(new OffTheRoadDaysFromSpec(DateOnly.MinValue), cancellationToken))
        {
            LogAlreadySeeded();
            return;
        }

        var mediator = services.GetRequiredService<IMediator>();
        var day = demo.DayFor(horizon.Today(time.GetUtcNow()));

        foreach (var order in demo.TransportOrders)
        {
            var booking = demo.BookingFor(day, order, horizon.Zone);
            var submitted = Succeeded(await mediator.Send(
                new SubmitTransportOrderCommand(booking, IntakeChannel.ShipperApi, CorrelationId.New().Value), cancellationToken), order.ShipperReference);
            if (submitted.TransportOrderNumber is not { } number)
            {
                throw new InvalidOperationException($"The TMS rejected the demo booking {order.ShipperReference}.");
            }

            Succeeded(await mediator.Send(
                new AssignTransportOrderCommand(number, CarrierId: null, $"demo-{order.ShipperReference}-{day:yyyyMMdd}", CorrelationId.New().Value), cancellationToken), number);
        }

        Succeeded(await mediator.Send(
            new RecordOffTheRoadCommand(demo.VehicleId, day, day, Enum.Parse<OffTheRoadReason>(demo.OffTheRoad.Reason), demo.OffTheRoad.Remark), cancellationToken), demo.OffTheRoad.VehicleId);
        Succeeded(await mediator.Send(
            new RecordAbsenceCommand(demo.DriverId, day, day, Enum.Parse<AbsenceReason>(demo.Absence.Reason), demo.Absence.Remark), cancellationToken), demo.Absence.DriverId);

        LogSeeded(day, demo.TransportOrders.Count);
    }

    private static T Succeeded<T>(Result<T> result, string what) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Seeding the demo's Over capacity day failed at {what}: {result.Status}.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the demo's Over capacity day {Day:yyyy-MM-dd} with {OrderCount} Own fleet Transport Orders")]
    private partial void LogSeeded(DateOnly day, int orderCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS already holds Absences or Off the road days, so the demo's Over capacity day isn't seeded")]
    private partial void LogAlreadySeeded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding the demo's Over capacity day failed; trying again at the next look")]
    private partial void LogFailed(Exception exception);
}
