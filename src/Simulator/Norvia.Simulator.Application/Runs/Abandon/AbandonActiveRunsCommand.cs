using Mediator;

namespace Norvia.Simulator.Application.Runs.Abandon;

/// <summary>
/// A run can be cancelled but not resumed after a restart, so a run still marked running when the Simulator starts is
/// marked failed: nothing is sending it any more.
/// </summary>
public sealed record AbandonActiveRunsCommand : ICommand<int>;
