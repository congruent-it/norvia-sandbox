using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>
/// Whether the plan's pools of Carrier texts are written by the personas agent (ADR 0040). Off by default: the pools are then the built-in
/// ones. The agent is called while a plan is made, never while a run is sending, so a run is never slowed or stopped by it.
/// </summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }

    /// <summary>The cap on pools one plan may ask the agent for (a plan has a pool for each profile and kind of text).</summary>
    [Range(0, 100)]
    public int MaxGenerationsPerPlan { get; set; } = 8;

    /// <summary>How many texts a pool is asked to hold.</summary>
    [Range(3, 50)]
    public int PoolSize { get; set; } = 8;

    /// <summary>A short timeout and no retry: an Agent is never on a critical path.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}
