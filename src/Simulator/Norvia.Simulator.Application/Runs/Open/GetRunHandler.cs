using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Open;

public sealed class GetRunHandler(IReadRepository<SimulationRun> runs, IReadRepository<LogEntry> log)
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
        var outcomes = answers.GroupBy(outcome => outcome)
            .Select(group => new OutcomeCount(group.Key, group.Count()))
            .OrderBy(count => count.Outcome)
            .ToList();
        var actors = run.Plan.Bookings.Select(booking => booking.PersonaName).Distinct().Order(StringComparer.Ordinal).ToList();
        return new RunDetail(RunSummary.From(run, answers.Count), outcomes, actors);
    }
}
