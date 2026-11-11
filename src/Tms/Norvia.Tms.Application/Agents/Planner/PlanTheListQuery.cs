using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Plan the waiting list (#445): the Planner agent works through the Transport Orders waiting for planning in the list's order, one question
/// (and one tool call) per order, with the what-if tally of the orders it has already suggested for Own fleet. Each Suggestion stands on
/// its own; none is assigned, pre-selected or stored.
/// </summary>
public sealed record PlanTheListQuery : IQuery<Result<PlannedList>>;
