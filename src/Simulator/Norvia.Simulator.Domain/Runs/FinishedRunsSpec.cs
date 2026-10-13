using Ardalis.Specification;

namespace Norvia.Simulator.Domain.Runs;

public sealed class FinishedRunsSpec : Specification<SimulationRun>
{
    public FinishedRunsSpec() => Query.Where(run => run.Status != SimulationRunStatus.Running);
}
