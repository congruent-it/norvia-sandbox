namespace Norvia.Tms.Application.Agents.Planner;

public enum PlanStopReason
{
    /// <summary>The run reached its cap on orders (and so on tool calls).</summary>
    CapReached = 1,

    /// <summary>The assistant was unavailable, or a tool was refused, for the next order.</summary>
    AssistantUnavailable,
}
