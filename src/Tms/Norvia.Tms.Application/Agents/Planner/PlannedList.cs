namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>What a run of <see cref="PlanTheListQuery"/> made: a Suggestion per order it reached, in the list's order, and why it stopped early, if it did.</summary>
public sealed record PlannedList(IReadOnlyList<ListedSuggestion> Suggestions, PlanStop? Stopped);
