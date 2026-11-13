using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The Shipper tracking assistant has no tool and its identity no role (ADR 0049).</summary>
public sealed class AssistantAccessCheck : Check
{
    public const string CheckId = "ASSISTANT-ACCESS";

    public override string Id => CheckId;

    public override string Rule => "The Shipper tracking assistant lists no tool and its Agent identity holds no role (ADR 0049).";

    public override string Title => "The tracking assistant can read the estate";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "assistantIdentityHoldsRoleOrTool";

    public override string Fix =>
        "Set assistantIdentityHoldsRoleOrTool to false and publish the Agent again: its definition lists no tool and assign-agent-roles.ps1 gives it no role.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        if (estate.Agents?.TrackingAssistant is not { } assistant)
        {
            return [];
        }

        return
        [
            .. assistant.IdentityRoles.Order(StringComparer.Ordinal).Select(role => new Evidence(TrackingAssistantFacts.AgentName, $"The Agent's identity holds {role}, and it must hold no role.")),
            .. assistant.Tools.Order(StringComparer.Ordinal).Select(tool => new Evidence(TrackingAssistantFacts.AgentName, $"The Agent lists the tool {tool}, and it must have none.")),
        ];
    }
}
