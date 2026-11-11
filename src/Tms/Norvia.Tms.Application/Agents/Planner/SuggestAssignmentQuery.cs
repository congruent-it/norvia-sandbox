using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>Ask the Planner agent who should carry a Transport Order waiting for planning. A Suggestion shown for the session: nothing is pre-selected, assigned or stored.</summary>
/// <param name="AlreadyAssumed">The what-if tally of a plan of the list (#445): the orders already suggested for Own fleet. Null or empty for a single question.</param>
public sealed record SuggestAssignmentQuery(string TransportOrderNumber, IReadOnlyList<string>? AlreadyAssumed = null) : IQuery<Result<Suggestion>>;
