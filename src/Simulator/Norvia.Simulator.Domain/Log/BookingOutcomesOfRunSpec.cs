using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>How Norvia answered each booking a run sent, one outcome per booking.</summary>
public sealed class BookingOutcomesOfRunSpec : Specification<LogEntry, LogOutcome>
{
    public BookingOutcomesOfRunSpec(SimulationRunId runId)
    {
        // An array, which EF translates to IN.
        LogOutcome[] outcomes = [.. LogOutcomes.Booking];
        Query.Where(entry => entry.RunId == runId && outcomes.Contains(entry.Outcome))
            .AsNoTracking()
            .Select(entry => entry.Outcome);
    }
}
