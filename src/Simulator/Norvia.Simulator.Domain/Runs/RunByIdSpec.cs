using Ardalis.Specification;

namespace Norvia.Simulator.Domain.Runs;

public sealed class RunByIdSpec : Specification<SimulationRun>
{
    public RunByIdSpec(SimulationRunId id) => Query.Where(run => run.Id == id);
}
