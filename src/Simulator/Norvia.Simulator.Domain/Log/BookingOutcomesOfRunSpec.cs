using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>How Norvia answered each booking a run sent, one outcome per booking.</summary>
public sealed class BookingOutcomesOfRunSpec : Specification<LogEntry, LogOutcome>
{
    public BookingOutcomesOfRunSpec(SimulationRunId runId) =>
        Query.Where(entry => entry.RunId == runId && entry.Outcome != LogOutcome.Info)
            .AsNoTracking()
            .Select(entry => entry.Outcome);
}
