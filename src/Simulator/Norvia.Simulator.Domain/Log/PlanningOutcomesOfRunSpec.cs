using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>How the TMS answered each call the Planner made in a run: one outcome per assignment or reassignment.</summary>
public sealed class PlanningOutcomesOfRunSpec : Specification<LogEntry, LogOutcome>
{
    public PlanningOutcomesOfRunSpec(SimulationRunId runId)
    {
        // An array, which EF translates to IN.
        LogOutcome[] outcomes = [.. LogOutcomes.Planning];
        Query.Where(entry => entry.RunId == runId && outcomes.Contains(entry.Outcome))
            .AsNoTracking()
            .Select(entry => entry.Outcome);
    }
}
