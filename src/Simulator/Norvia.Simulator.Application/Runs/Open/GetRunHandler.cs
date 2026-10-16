using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Open;

public sealed class GetRunHandler(IReadRepository<SimulationRun> runs, IReadRepository<LogEntry> log, IWorldSource world)
    : IQueryHandler<GetRunQuery, Result<RunDetail>>
{
    public async ValueTask<Result<RunDetail>> Handle(GetRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var run = await runs.FirstOrDefaultAsync(new RunByIdSpec(query.RunId), cancellationToken);
        if (run is null)
        {
            return Result.NotFound($"There is no run {query.RunId.Value}.");
        }

        var answers = await log.ListAsync(new BookingOutcomesOfRunSpec(query.RunId), cancellationToken);
        var planning = await log.ListAsync(new PlanningOutcomesOfRunSpec(query.RunId), cancellationToken);
        var carriers = await log.ListAsync(new CarrierOutcomesOfRunSpec(query.RunId), cancellationToken);
        var drivers = await log.ListAsync(new DriverOutcomesOfRunSpec(query.RunId), cancellationToken);
        var outcomes = answers.Concat(planning).Concat(carriers).Concat(drivers).GroupBy(outcome => outcome)
            .Select(group => new OutcomeCount(group.Key, group.Count()))
            .OrderBy(count => count.Outcome)
            .ToList();
        var actors = run.Plan.Bookings.Select(booking => booking.PersonaName).Distinct().Order(StringComparer.Ordinal).ToList();
        if (run.Plan.Choices.Count > 0)
        {
            actors.Add(LogEntry.PlannerActor);
            if (run.Settings.Carriers.ReleasePercent > 0)
            {
                // The Carriers act as their Personas, once the Planner has released what went to them.
                actors.AddRange(world.Load().ActiveCarriers.Where(carrier => carrier.HasPersona).Select(carrier => carrier.PersonaName!));
            }
            if (run.Settings.OwnFleetPercent > 0)
            {
                // The Drivers act as themselves, on the Trips for today.
                actors.AddRange(world.Load().OwnFleetDrivers.Select(driver => driver.Name));
            }
        }

        return new RunDetail(RunSummary.From(run, answers.Count, planning.Count(outcome => outcome == LogOutcome.Planned)), outcomes, actors, run.Settings);
    }
}
