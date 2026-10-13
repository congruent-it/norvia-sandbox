using Ardalis.Result;
using Mediator;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Open;

public sealed record GetRunQuery(SimulationRunId RunId) : IQuery<Result<RunDetail>>;
