namespace Norvia.Simulator.Application.Plans.Pools;

/// <summary>The Agents' settings that matter to a plan: whether they are on, how many pools one plan may ask for, and how many texts a pool holds.</summary>
public sealed record PersonaSettings(bool AgentsEnabled, int MaxGenerationsPerPlan, int PoolSize)
{
    /// <summary>Agents off: every pool is the built-in one.</summary>
    public static PersonaSettings BuiltInOnly { get; } = new(AgentsEnabled: false, MaxGenerationsPerPlan: 0, PoolSize: 8);
}
