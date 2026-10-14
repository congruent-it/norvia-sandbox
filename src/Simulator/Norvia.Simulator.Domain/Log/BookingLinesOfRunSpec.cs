using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>The lines of a run that are bookings sent, not the Simulator's own notes or the Planner's calls.</summary>
public sealed class BookingLinesOfRunSpec : Specification<LogEntry>
{
    public BookingLinesOfRunSpec(SimulationRunId runId)
    {
        // An array, which EF translates to IN.
        LogOutcome[] outcomes = [.. LogOutcomes.Booking];
        Query.Where(entry => entry.RunId == runId && outcomes.Contains(entry.Outcome)).AsNoTracking();
    }
}
