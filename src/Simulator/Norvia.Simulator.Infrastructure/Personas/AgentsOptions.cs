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

    /// <summary>
    /// The cost cap of a plan, as the most output tokens its generations may be allowed to write together. Each generation is capped at the
    /// definition's <c>maxOutputTokens</c>, so the plan makes no more generations than fit in it, whatever <see cref="MaxGenerationsPerPlan"/> says.
    /// </summary>
    [Range(0, 200_000)]
    public int MaxOutputTokensPerPlan { get; set; } = 8_000;

    /// <summary>How many texts a pool is asked to hold.</summary>
    [Range(3, 50)]
    public int PoolSize { get; set; } = 8;

    /// <summary>A short timeout and no retry: an Agent is never on a critical path.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;

    public FoundryOptions Foundry { get; set; } = new();

    /// <summary>Whether the agent runs in Foundry, which is the case wherever the estate gave the Simulator a project endpoint.</summary>
    public bool UsesFoundry => Foundry.ProjectEndpoint is not null;

    /// <summary>The Entra client ID of the Simulator's managed identity, which asks the agent in Azure.</summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}

/// <summary>The Foundry project the personas agent is published in (ADR 0040), which the Simulator calls as its own managed identity.</summary>
public sealed class FoundryOptions
{
    /// <summary>The project's endpoint, such as <c>https://&lt;account&gt;.services.ai.azure.com/api/projects/norvia</c>.</summary>
    public Uri? ProjectEndpoint { get; set; }

    /// <summary>The scope of the token the project takes.</summary>
    public string Scope { get; set; } = "https://ai.azure.com/.default";
}
