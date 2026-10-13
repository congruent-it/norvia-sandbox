using Ardalis.Specification;

namespace Norvia.Simulator.Domain.Runs;

public sealed class RunSummariesSpec : Specification<SimulationRun, RunListRow>
{
    public RunSummariesSpec() =>
        Query.OrderByDescending(run => run.StartedAt)
            .AsNoTracking()
            .Select(run => new RunListRow(
                run.Id.Value, run.Number, run.Status, run.StartedAt, run.FinishedAt, run.StartedBy,
                run.Seed, run.PresetName, run.Mode, run.PlannedCount));
}
