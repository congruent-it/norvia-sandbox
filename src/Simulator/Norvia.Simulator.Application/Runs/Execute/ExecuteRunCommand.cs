using Ardalis.Result;
using Mediator;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// Sends a started run's plan to the Partner API. <paramref name="StopSending"/> is signalled when the run is cancelled: the
/// bookings in flight then land and are logged, and no more are sent.
/// </summary>
public sealed record ExecuteRunCommand(SimulationRunId RunId, CancellationToken StopSending) : ICommand<Result>;
