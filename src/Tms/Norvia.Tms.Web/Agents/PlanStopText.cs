using Norvia.Tms.Application.Agents.Planner;

namespace Norvia.Tms.Web.Agents;

/// <summary>What the page tells the Planner when a run of Plan the list stopped before the end of the list.</summary>
internal static class PlanStopText
{
    public static string Of(PlannedList plan, PlanStop stop)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stop);
        return stop.Reason switch
        {
            PlanStopReason.CapReached =>
                $"The run stopped at its cap after {plan.Suggestions.Count} order(s): {stop.OrdersLeft} order(s), from {stop.NextOrder} on, have no suggestion. Plan the list again once these are dealt with.",
            _ => $"The assistant became unavailable after {plan.Suggestions.Count} order(s): {stop.OrdersLeft} order(s), from {stop.NextOrder} on, have no suggestion.",
        };
    }
}
