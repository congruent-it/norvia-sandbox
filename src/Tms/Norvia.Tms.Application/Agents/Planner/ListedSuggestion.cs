using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>The Suggestion for one order of the list. Accepted or ignored on its own: accepting one accepts none of the others.</summary>
public sealed record ListedSuggestion(string TransportOrderNumber, Suggestion Suggestion);
