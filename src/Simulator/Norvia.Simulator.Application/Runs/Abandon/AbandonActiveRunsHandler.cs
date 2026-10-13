using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Abandon;

public sealed class AbandonActiveRunsHandler(IRepository<SimulationRun> runs, TimeProvider time)
    : ICommandHandler<AbandonActiveRunsCommand, int>
{
    public async ValueTask<int> Handle(AbandonActiveRunsCommand command, CancellationToken cancellationToken)
    {
        var active = await runs.ListAsync(new ActiveRunSpec(), cancellationToken);
        var now = time.GetUtcNow();
        foreach (var run in active)
        {
            run.Fail(now);
        }

        await runs.UpdateRangeAsync(active, cancellationToken);
        return active.Count;
    }
}
