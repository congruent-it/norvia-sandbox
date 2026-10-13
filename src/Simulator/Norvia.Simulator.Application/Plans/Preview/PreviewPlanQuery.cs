using Ardalis.Result;
using Mediator;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>The plan the settings would give, without sending anything. It matches the plan a run started now would send.</summary>
public sealed record PreviewPlanQuery(SimulationSettings Settings) : IQuery<Result<PlanPreview>>;
