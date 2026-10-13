using Ardalis.Specification;

namespace Norvia.Simulator.Domain.Runs;

public sealed class RunSequenceSpec : Specification<RunSequence>
{
    public RunSequenceSpec() => Query.Take(1);
}
