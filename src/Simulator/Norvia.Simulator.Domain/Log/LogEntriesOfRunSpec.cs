using Ardalis.Specification;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>A run's log lines in the order they happened, narrowed by the filter and, when polling, to those after a line already shown.</summary>
public sealed class LogEntriesOfRunSpec : Specification<LogEntry>
{
    public LogEntriesOfRunSpec(SimulationRunId runId, LogFilter filter, int afterSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(filter);

        Query.Where(entry => entry.RunId == runId && entry.Sequence > afterSequence);
        if (!string.IsNullOrWhiteSpace(filter.Actor))
        {
            var actor = filter.Actor.Trim();
            Query.Where(entry => entry.Actor == actor);
        }

        if (filter.Outcome is { } outcome)
        {
            Query.Where(entry => entry.Outcome == outcome);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            // Contains is a LIKE in SQL Server, whose default collation ignores case.
            var text = filter.Text.Trim();
            Query.Where(entry =>
                (entry.CorrelationId != null && entry.CorrelationId.Contains(text))
                || (entry.ShipperReference != null && entry.ShipperReference.Contains(text))
                || (entry.TransportOrderNumber != null && entry.TransportOrderNumber.Contains(text)));
        }

        Query.OrderBy(entry => entry.Sequence).AsNoTracking();
    }
}
