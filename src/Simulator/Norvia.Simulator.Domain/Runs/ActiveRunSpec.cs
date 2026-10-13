using Ardalis.Specification;

namespace Norvia.Simulator.Domain.Runs;

public sealed class ActiveRunSpec : Specification<SimulationRun>
{
    public ActiveRunSpec() => Query.Where(run => run.Status == SimulationRunStatus.Running);
}
