using Ardalis.Result;
using Mediator;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Start;

/// <summary>Makes the plan for the settings and starts sending it. Only one run is active at a time, and the caps apply.</summary>
public sealed record StartRunCommand(SimulationSettings Settings, string StartedBy) : ICommand<Result<RunSummary>>;
