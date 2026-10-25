namespace Norvia.HealthCheck.Application.Agents;

/// <summary>
/// Whether the Agents are on (ADR 0040). Off unless the estate or the AppHost turns it on; while off, the page shows no narration and
/// the app maps no agent API.
/// </summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }
}
