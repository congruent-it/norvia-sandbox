using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>The lines of a run that are bookings sent, not the Simulator's own notes.</summary>
public sealed class BookingLinesOfRunSpec : Specification<LogEntry>
{
    public BookingLinesOfRunSpec(SimulationRunId runId) =>
        Query.Where(entry => entry.RunId == runId && entry.Outcome != LogOutcome.Info).AsNoTracking();
}
