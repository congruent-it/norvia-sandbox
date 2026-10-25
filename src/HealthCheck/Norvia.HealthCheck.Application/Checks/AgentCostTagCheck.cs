using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Everything an agent costs is tagged, so the cost shows.</summary>
public sealed class AgentCostTagCheck : Check
{
    public const string CheckId = "AGENT-COST-TAG";

    public override string Id => CheckId;

    public override string Rule => "Everything an agent costs is tagged, so the cost shows.";

    public override string Title => "An agent has no cost tag";

    public override Severity Severity => Severity.Low;

    public override string AsFoundToggle => "noAgentCostTag";

    public override string Fix =>
        "Set noAgentCostTag to false and provision the estate: the agent's resources then carry the cost tag.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. (estate.Agents?.Identities ?? [])
                .Where(agent => !agent.HasCostTag)
                .OrderBy(agent => agent.Name, StringComparer.Ordinal)
                .Select(agent => new Evidence(agent.Name, $"The agent's resources carry no cost tag, so what it costs does not show in the bill.")),
        ];
    }
}
