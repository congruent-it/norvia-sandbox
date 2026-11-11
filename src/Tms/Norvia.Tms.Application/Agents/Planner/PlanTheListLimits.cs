namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>The caps on one run of <see cref="PlanTheListQuery"/> (#369, decision 12), from <see cref="AgentsOptions.MaxPlanOrders"/>.</summary>
/// <param name="MaxOrders">The most orders one run works through. Each costs one question and, by the Planner agent's own cap, one tool call, so it caps the run's tool calls too.</param>
public sealed record PlanTheListLimits(int MaxOrders)
{
    public int MaxOrders { get; } = MaxOrders > 0 ? MaxOrders : throw new ArgumentOutOfRangeException(nameof(MaxOrders), "A run needs room for at least one order.");
}
