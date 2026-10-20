using Mediator;

namespace Norvia.IntegrationConsole.Application.Runs.Failed;

public sealed class ListFailedRunsHandler(IFailedRuns runs, TimeProvider time) : IQueryHandler<ListFailedRunsQuery, FailedRunList>
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public async ValueTask<FailedRunList> Handle(ListFailedRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var to = time.GetUtcNow();
        var from = to - Window;
        var recorded = await runs.FailedRunsAsync(from, cancellationToken);

        var failed = recorded
            .Where(run => run.Start >= from)
            .Select(run => new FailedRun(run.Flow ?? run.Run?.Workflow ?? "unknown", run.Run, run.Start, run.CorrelationId, run.FailedStep, run.Error))
            .OrderByDescending(run => run.Start)
            .ToList();
        var flows = failed.Select(run => run.Flow).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var flow = query.Flow?.Trim();
        var shown = string.IsNullOrEmpty(flow) ? failed : failed.Where(run => run.Flow == flow).ToList();
        return new FailedRunList(from, to, flows, shown);
    }
}
