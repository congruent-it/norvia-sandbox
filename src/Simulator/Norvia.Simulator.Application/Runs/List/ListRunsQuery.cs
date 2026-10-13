using Mediator;

namespace Norvia.Simulator.Application.Runs.List;

public sealed record ListRunsQuery : IQuery<IReadOnlyList<RunSummary>>;
