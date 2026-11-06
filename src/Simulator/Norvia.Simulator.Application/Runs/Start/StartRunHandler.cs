using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Plans.Pools;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Start;

public sealed class StartRunHandler(
    IRepository<SimulationRun> runs,
    IRepository<RunSequence> sequences,
    IRepository<LogEntry> log,
    IWorldSource world,
    IRunExecution execution,
    PlanPools pools,
    SimulationLimits limits,
    TimeProvider time)
    : ICommandHandler<StartRunCommand, Result<RunSummary>>
{
    public async ValueTask<Result<RunSummary>> Handle(StartRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var problems = command.Settings.Problems(limits);
        if (problems.Count > 0)
        {
            return Result.Invalid([.. problems.Select(problem => new ValidationError(problem))]);
        }

        if (!world.Load().Shippers.Any(shipper => shipper.Lanes.Count > 0))
        {
            return Result.Invalid(new ValidationError("No Shipper has a Rate Card with a Lane to book for."));
        }

        if (await runs.AnyAsync(new ActiveRunSpec(), cancellationToken))
        {
            return Result.Conflict("A run is already active. Cancel it or wait for it to finish.");
        }

        var sequence = await sequences.FirstOrDefaultAsync(new RunSequenceSpec(), cancellationToken);
        var isNew = sequence is null;
        sequence ??= RunSequence.Create();
        var number = sequence.Take();
        if (isNew)
        {
            await sequences.AddAsync(sequence, cancellationToken);
        }
        else
        {
            await sequences.UpdateAsync(sequence, cancellationToken);
        }

        var now = time.GetUtcNow();
        var plan = await pools.AddToAsync(BookingPlanGenerator.Generate(command.Settings, world.Load(), number), world.Load(), cancellationToken);
        var run = SimulationRun.Start(number, command.Settings, plan, now, command.StartedBy);
        await runs.AddAsync(run, cancellationToken);
        await log.AddAsync(LogEntry.Note(run.Id, 1, now, StartNote(number, plan, command.Settings)), cancellationToken);

        execution.Start(run.Id);
        return RunSummary.From(run, sent: 0);
    }

    // A hostile run says so on its first line, so it is never mistaken for a normal one (profile: hostile).
    public static string StartNote(int number, SimulationPlan plan, SimulationSettings settings)
    {
        var note = $"Run {number} started: {plan.Bookings.Count} bookings, seed {settings.Seed}";
        if (!plan.IsHostile)
        {
            return note;
        }

        var writers = new List<string>();
        if (plan.HostileCarrierIds.Count > 0)
        {
            writers.Add($"Carriers {string.Join(", ", plan.HostileCarrierIds)}");
        }

        if (plan.HostileDriverIds.Count > 0)
        {
            writers.Add($"Drivers {string.Join(", ", plan.HostileDriverIds)}");
        }

        // A log line's action is at most 300 characters.
        var hostile = $"{note}. HOSTILE RUN (profile: hostile): {string.Join(" and ", writers)} send instruction-like remarks that try to instruct the other Agents";
        return hostile.Length <= 300 ? hostile : $"{hostile[..297]}...";
    }
}
