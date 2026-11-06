using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Every agent identity holds only a role that reads (ADR 0040).</summary>
public sealed class AgentRoleCheck : Check
{
    public const string CheckId = "AGENT-ROLE";

    public override string Id => CheckId;

    public override string Rule => "Every agent identity holds only a role that reads (ADR 0040).";

    public override string Title => "An agent identity holds a role wider than read";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "agentRoleWiderThanRead";

    public override string Fix =>
        "Set agentRoleWiderThanRead to false and provision the estate, then assign-agent-roles.ps1 gives the identity its read role alone.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. (estate.Agents?.Identities ?? [])
                .Where(agent => !agent.RoleIsRead)
                .OrderBy(agent => agent.Name, StringComparer.Ordinal)
                .Select(agent => new Evidence(agent.Name, $"The identity holds {agent.Role}, which is wider than read.")),
        ];
    }
}
