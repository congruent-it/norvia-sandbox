using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Cancel;

/// <summary>Marks the run cancelled and tells its executor to stop dispatching; the executor writes the closing line of the log once what was in flight has landed.</summary>
public sealed class CancelRunHandler(IRepository<SimulationRun> runs, IRunExecution execution, TimeProvider time)
    : ICommandHandler<CancelRunCommand, Result>
{
    public async ValueTask<Result> Handle(CancelRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var run = await runs.FirstOrDefaultAsync(new RunByIdSpec(command.RunId), cancellationToken);
        if (run is null)
        {
            return Result.NotFound($"There is no run {command.RunId.Value}.");
        }

        if (!run.Cancel(time.GetUtcNow()))
        {
            return Result.Conflict("The run has already finished.");
        }

        await runs.UpdateAsync(run, cancellationToken);
        execution.Cancel(run.Id);
        return Result.Success();
    }
}
