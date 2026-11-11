namespace Norvia.Tms.Application.Agents;

/// <summary>
/// Whether the Agents are on (ADR 0040). Off unless the estate or the AppHost turns them on; while off, the TMS UI shows no
/// Explain button and maps no agent API.
/// </summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }

    /// <summary>The most orders one "Plan the list" run works through (#445); one tool call each, so it caps the run's tool calls too. The rest are left for the next run.</summary>
    public int MaxPlanOrders { get; set; } = 10;
}
