using Mediator;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Log.List;

/// <summary>A run's log, in the order it happened, narrowed by the filter.</summary>
public sealed record ListLogQuery(SimulationRunId RunId, LogFilter Filter) : IQuery<IReadOnlyList<LogLine>>;
