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
        var all = await runs.ListAsync(new RunsNewestFirstSpec(), cancellationToken);
        var summaries = new List<RunSummary>(all.Count);
        foreach (var run in all)
        {
            summaries.Add(RunSummary.From(run, await SentAsync(log, run.Id, cancellationToken)));
        }

        return summaries;
    }

    internal static async Task<int> SentAsync(IReadRepository<LogEntry> log, SimulationRunId runId, CancellationToken cancellationToken) =>
        await log.CountAsync(new BookingLinesOfRunSpec(runId), cancellationToken);
}
