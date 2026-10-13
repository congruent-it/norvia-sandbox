using Ardalis.Result;
using Mediator;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Cancel;

/// <summary>Stops a run from sending any more. What it already sent stays in the estate.</summary>
public sealed record CancelRunCommand(SimulationRunId RunId) : ICommand<Result>;
