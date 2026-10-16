using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>How the Driver app answered each call the Drivers made in a run: one outcome per report.</summary>
public sealed class DriverOutcomesOfRunSpec : Specification<LogEntry, LogOutcome>
{
    public DriverOutcomesOfRunSpec(SimulationRunId runId)
    {
        // An array, which EF translates to IN.
        LogOutcome[] outcomes = [.. LogOutcomes.Driver];
        Query.Where(entry => entry.RunId == runId && outcomes.Contains(entry.Outcome))
            .AsNoTracking()
            .Select(entry => entry.Outcome);
    }
}
