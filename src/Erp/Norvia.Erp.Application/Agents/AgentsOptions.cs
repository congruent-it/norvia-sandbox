namespace Norvia.Erp.Application.Agents;

/// <summary>
/// Whether the Agents are on (ADR 0040). Off unless the estate or the AppHost turns them on; while off, the ERP UI shows no Explain
/// button.
/// </summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }
}
