using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Tms.Application.TransportOrders.ToPlan;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Works through the waiting list in its own order (the earliest collection first), asking the Planner agent about one order at a time with
/// the orders it has already suggested for Own fleet as the what-if tally. The TMS query counts the tally (#445), so the agent does no
/// arithmetic. The run is capped at <see cref="PlanTheListLimits.MaxOrders"/> orders; a run that hits its cap, or whose assistant fails
/// part way, stops with the Suggestions made so far and says so. Nothing is executed.
/// </summary>
public sealed class PlanTheListHandler(ISender sender, PlanTheListLimits limits) : IQueryHandler<PlanTheListQuery, Result<PlannedList>>
{
    public async ValueTask<Result<PlannedList>> Handle(PlanTheListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var waiting = await sender.Send(new ListTransportOrdersToPlanQuery(), cancellationToken);
        var cap = limits.MaxOrders;
        var suggestions = new List<ListedSuggestion>();
        var onOwnFleet = new List<string>();

        for (var index = 0; index < waiting.Count; index++)
        {
            var number = waiting[index].TransportOrderNumber;
            if (index >= cap)
            {
                return new PlannedList(suggestions, new PlanStop(PlanStopReason.CapReached, waiting.Count - index, number));
            }

            var result = await sender.Send(new SuggestAssignmentQuery(number, [.. onOwnFleet]), cancellationToken);
            if (result.Status == ResultStatus.NotFound)
            {
                // Planned or removed since the list was read: nothing to suggest for it.
                continue;
            }

            if (!result.IsSuccess)
            {
                return new PlannedList(suggestions, new PlanStop(PlanStopReason.AssistantUnavailable, waiting.Count - index, number));
            }

            suggestions.Add(new ListedSuggestion(number, result.Value));
            if (result.Value.Action == SuggestedActionKind.AssignOwnFleet)
            {
                onOwnFleet.Add(number);
            }
        }

        return new PlannedList(suggestions, null);
    }
}
