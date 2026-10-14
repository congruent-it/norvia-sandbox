using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Runs.Open;

/// <summary>A run with how Norvia and the TMS have answered so far, and the actors that appear in its plan, for the log's filter.</summary>
public sealed record RunDetail(RunSummary Run, IReadOnlyList<OutcomeCount> Outcomes, IReadOnlyList<string> Actors);

public sealed record OutcomeCount(LogOutcome Outcome, int Count);
