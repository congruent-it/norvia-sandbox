using Ardalis.Specification;

namespace Norvia.Simulator.Domain.Log;

public sealed class AllLogEntriesSpec : Specification<LogEntry>
{
    public AllLogEntriesSpec() => Query.OrderBy(entry => entry.Sequence);
}
