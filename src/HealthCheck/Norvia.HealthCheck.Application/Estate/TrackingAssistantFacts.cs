namespace Norvia.HealthCheck.Application.Estate;

/// <summary>
/// What guards the Shipper tracking assistant (ADR 0049): the Partner portal's limits and its check of the answer, and what its Agent identity holds.
/// The Agent is meant to have no tool and its identity no role at all, so there is nothing for it to read beyond what the portal gave it.
/// </summary>
/// <param name="LimitsOn">The per-Persona rate and size limits and the daily token budgets are enforced.</param>
/// <param name="OutputCheckOn">The portal checks the answer's fields and values against the data before the Shipper sees it.</param>
/// <param name="IdentityRoles">The roles the Agent's identity holds. None is right.</param>
/// <param name="Tools">The tools the published Agent lists. None is right.</param>
public sealed record TrackingAssistantFacts(
    bool LimitsOn = true,
    bool OutputCheckOn = true,
    IReadOnlyList<string>? IdentityRoles = null,
    IReadOnlyList<string>? Tools = null)
{
    public const string AgentName = "shipper-tracking-assistant";

    public IReadOnlyList<string> IdentityRoles { get; init; } = IdentityRoles ?? [];

    public IReadOnlyList<string> Tools { get; init; } = Tools ?? [];
}
