using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.ClearHistory;

public sealed class ClearHistoryHandler(IRepository<SimulationRun> runs, IRepository<LogEntry> log)
    : ICommandHandler<ClearHistoryCommand, Result>
{
    public async ValueTask<Result> Handle(ClearHistoryCommand command, CancellationToken cancellationToken)
    {
        if (await runs.AnyAsync(new ActiveRunSpec(), cancellationToken))
        {
            return Result.Conflict("A run is active. Cancel it or wait for it to finish before clearing the history.");
        }

        await log.DeleteRangeAsync(await log.ListAsync(new AllLogEntriesSpec(), cancellationToken), cancellationToken);
        await runs.DeleteRangeAsync(await runs.ListAsync(new FinishedRunsSpec(), cancellationToken), cancellationToken);
        return Result.Success();
    }
}
