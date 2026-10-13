using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.List;

public sealed class ListRunsHandler(IReadRepository<SimulationRun> runs, IReadRepository<LogEntry> log)
    : IQueryHandler<ListRunsQuery, IReadOnlyList<RunSummary>>
{
    public async ValueTask<IReadOnlyList<RunSummary>> Handle(ListRunsQuery query, CancellationToken cancellationToken)
    {
        var rows = await runs.ListAsync(new RunSummariesSpec(), cancellationToken);
        var summaries = new List<RunSummary>(rows.Count);
        foreach (var row in rows)
        {
            summaries.Add(RunSummary.From(row, await log.CountAsync(new BookingLinesOfRunSpec(new SimulationRunId(row.Id)), cancellationToken)));
        }

        return summaries;
    }
}
