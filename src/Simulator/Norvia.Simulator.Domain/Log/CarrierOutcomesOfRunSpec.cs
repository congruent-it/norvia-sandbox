using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>How Norvia answered each call the Carriers made in a run: one outcome per answer, tracking message or Proof of delivery.</summary>
public sealed class CarrierOutcomesOfRunSpec : Specification<LogEntry, LogOutcome>
{
    public CarrierOutcomesOfRunSpec(SimulationRunId runId)
    {
        // An array, which EF translates to IN.
        LogOutcome[] outcomes = [.. LogOutcomes.Carrier];
        Query.Where(entry => entry.RunId == runId && outcomes.Contains(entry.Outcome))
            .AsNoTracking()
            .Select(entry => entry.Outcome);
    }
}
