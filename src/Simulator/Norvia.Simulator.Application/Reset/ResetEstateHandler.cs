using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Reset;

/// <summary>
/// Checks quiet first (no active run, every application willing and its subscriptions empty), then resets each application through its own door,
/// then clears the Simulator's own runs, logs and run counter last: an application that refuses leaves the history to explain what happened.
/// </summary>
public sealed class ResetEstateHandler(IRepository<SimulationRun> runs, IRepository<LogEntry> log, IRepository<RunSequence> sequences, IEstateReset doors)
    : ICommandHandler<ResetEstateCommand, Result<EstateResetReport>>
{
    public async ValueTask<Result<EstateResetReport>> Handle(ResetEstateCommand command, CancellationToken cancellationToken)
    {
        var readiness = await EstateReadinessCheck.RunAsync(runs, doors, cancellationToken);
        if (readiness.Reason is { } reason)
        {
            return Result.Conflict(reason);
        }

        // Every application is told, even after one refuses: a reset that stopped at the first would leave the rest as dirty as it found them.
        var results = new List<AppResetResult>();
        foreach (var app in EstateApps.All)
        {
            results.Add(await doors.ResetAsync(app, cancellationToken));
        }

        if (results.Any(result => !result.Succeeded))
        {
            return new EstateResetReport(results, []);
        }

        var entries = await log.ListAsync(new AllLogEntriesSpec(), cancellationToken);
        var finished = await runs.ListAsync(new FinishedRunsSpec(), cancellationToken);
        var counter = await sequences.ListAsync(new RunSequenceSpec(), cancellationToken);
        await log.DeleteRangeAsync(entries, cancellationToken);
        await runs.DeleteRangeAsync(finished, cancellationToken);
        await sequences.DeleteRangeAsync(counter, cancellationToken);

        return new EstateResetReport(results, [new DeletedKind("Runs", finished.Count), new DeletedKind("Log lines", entries.Count)]);
    }
}
