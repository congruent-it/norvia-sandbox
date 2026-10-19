using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>How the ERP and the Shippers' inboxes answered each step of invoicing in a run: one outcome per step.</summary>
public sealed class InvoicingOutcomesOfRunSpec : Specification<LogEntry, LogOutcome>
{
    public InvoicingOutcomesOfRunSpec(SimulationRunId runId)
    {
        // An array, which EF translates to IN.
        LogOutcome[] outcomes = [.. LogOutcomes.Invoicing];
        Query.Where(entry => entry.RunId == runId && outcomes.Contains(entry.Outcome))
            .AsNoTracking()
            .Select(entry => entry.Outcome);
    }
}
