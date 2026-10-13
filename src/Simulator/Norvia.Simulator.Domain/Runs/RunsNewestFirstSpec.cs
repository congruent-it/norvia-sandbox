using Ardalis.Specification;

namespace Norvia.Simulator.Domain.Runs;

public sealed class RunsNewestFirstSpec : Specification<SimulationRun>
{
    public RunsNewestFirstSpec() => Query.OrderByDescending(run => run.StartedAt).AsNoTracking();
}
