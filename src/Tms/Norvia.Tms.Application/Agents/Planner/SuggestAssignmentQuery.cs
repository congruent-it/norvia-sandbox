using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>Ask the Planner agent who should carry a Transport Order waiting for planning. A Suggestion shown for the session: nothing is pre-selected, assigned or stored.</summary>
public sealed record SuggestAssignmentQuery(string TransportOrderNumber) : IQuery<Result<Suggestion>>;
